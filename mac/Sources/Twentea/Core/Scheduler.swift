import Foundation

/// Shows and hides break UI. The scheduler only decides when and how strongly.
@MainActor
protocol BreakPresenting: AnyObject {
    func show(_ session: BreakSession)
    func update(_ session: BreakSession)
    func hide()
}

struct BreakSession: Equatable {
    var kind: BreakKind
    var level: Enforcement
    var startedAt: Date
    var endsAt: Date
    var canPostpone: Bool
    var skipAvailableAt: Date?  // nil means skipping is not allowed
}

enum Phase: Equatable {
    case off
    case offHours
    case paused(until: Date?)
    case smartPaused(String)
    case working
    case due(BreakKind, since: Date)
    case onBreak(BreakSession)
}

/// The break state machine. `tick` runs once a second.
@MainActor
final class Scheduler: ObservableObject {
    @Published private(set) var phase: Phase = .working
    @Published private(set) var microElapsed: TimeInterval = 0
    @Published private(set) var longElapsed: TimeInterval = 0
    @Published private(set) var now = Date()

    /// Active time only counts while the user was active within this window.
    static let activeIdleLimit: TimeInterval = 120
    /// A due break starts once input pauses this long.
    static let breakpointIdle: TimeInterval = 3

    var settings: AppSettings

    /// Master switch. Off ends any break without logging it and stops all timers.
    var isOn = true {
        didSet {
            guard isOn != oldValue else { return }
            if isOn {
                phase = .working
            } else {
                presenter?.hide()
                phase = .off
            }
        }
    }
    private let activity: ActivitySource
    private weak var presenter: BreakPresenting?
    private let notifier: Notifying?
    private let stats: StatsLog?

    private var lastTick: Date?
    private var postponesUsed: [BreakKind: Int] = [:]
    private var loop: Task<Void, Never>?

    init(settings: AppSettings, activity: ActivitySource, presenter: BreakPresenting?,
         notifier: Notifying? = nil, stats: StatsLog? = nil) {
        self.settings = settings
        self.activity = activity
        self.presenter = presenter
        self.notifier = notifier
        self.stats = stats
    }

    func start() {
        loop?.cancel()
        loop = Task { [weak self] in
            while !Task.isCancelled {
                self?.tick(Date())
                try? await Task.sleep(for: .seconds(1))
            }
        }
    }

    // MARK: - Derived values

    func remaining(_ kind: BreakKind) -> TimeInterval? {
        let cfg = settings.config(for: kind)
        guard cfg.enabled else { return nil }
        let elapsed = kind == .micro ? microElapsed : longElapsed
        return max(0, TimeInterval(cfg.intervalMinutes * 60) - elapsed)
    }

    /// The break that comes first, and how long until it.
    var nextBreak: (kind: BreakKind, seconds: TimeInterval)? {
        let options = BreakKind.allCases.compactMap { k in remaining(k).map { (kind: k, seconds: $0) } }
        return options.min { $0.seconds < $1.seconds }
    }

    var postponesLeft: Int {
        guard case .onBreak(let s) = phase else { return 0 }
        return max(0, settings.config(for: s.kind).maxPostpones - postponesUsed[s.kind, default: 0])
    }

    // MARK: - Tick

    func tick(_ date: Date) {
        now = date
        guard isOn else {
            phase = .off
            lastTick = date
            return
        }
        let dt = min(max(date.timeIntervalSince(lastTick ?? date), 0), 5)
        lastTick = date

        if case .onBreak(var session) = phase {
            let cfg = settings.config(for: session.kind)
            // During a dim or overlay, touching the keyboard or mouse restarts the countdown.
            // Moving startedAt too keeps the progress ring in step with the new countdown.
            if settings.features.restartOnInput, session.level >= .softDim, activity.idleSeconds < 1,
               date.timeIntervalSince(session.startedAt) > 2 {
                session.startedAt = date
                session.endsAt = date.addingTimeInterval(TimeInterval(cfg.durationSeconds))
                phase = .onBreak(session)
                presenter?.update(session)
            }
            if date >= session.endsAt { finish(.done) }
            return
        }

        if case .paused(let until) = phase {
            if let until, date >= until { phase = .working } else { return }
        }

        guard settings.schedule.isActive(at: date) else {
            phase = .offHours
            return
        }

        if settings.features.smartPause, let reason = activity.smartPauseReason(settings.smartPause) {
            phase = .smartPaused(reason)
            return
        }

        let idle = activity.idleSeconds
        if settings.features.idleCredit { creditIdle(idle, at: date) }
        if idle < Self.activeIdleLimit {
            microElapsed += dt
            longElapsed += dt
        }

        if case .due(let kind, let since) = phase {
            let grace = TimeInterval(settings.breakpointGraceSeconds)
            if idle >= Self.breakpointIdle || activity.lastAppSwitch > since || date.timeIntervalSince(since) >= grace {
                startBreak(kind, at: date)
            }
            return
        }

        if let kind = dueKind() {
            phase = .due(kind, since: date)
        } else {
            phase = .working
        }
    }

    private func dueKind() -> BreakKind? {
        if settings.long.enabled, longElapsed >= TimeInterval(settings.long.intervalMinutes * 60) { return .long }
        if settings.micro.enabled, microElapsed >= TimeInterval(settings.micro.intervalMinutes * 60) { return .micro }
        return nil
    }

    /// Being away for a break's length counts as taking it. A short pause in input is usually reading,
    /// not looking away, so idle time only counts once it passes the active-idle limit too.
    private func creditIdle(_ idle: TimeInterval, at date: Date) {
        if settings.long.enabled, idle >= max(TimeInterval(settings.long.durationSeconds), Self.activeIdleLimit), longElapsed > 60 {
            stats?.record(.long, .credited, at: date)
            longElapsed = 0
            microElapsed = 0
        } else if settings.micro.enabled, idle >= max(TimeInterval(settings.micro.durationSeconds), Self.activeIdleLimit), microElapsed > 60 {
            stats?.record(.micro, .credited, at: date)
            microElapsed = 0
        }
    }

    // MARK: - Actions

    func startBreak(_ kind: BreakKind, at date: Date = Date()) {
        guard isOn else { return }
        let cfg = settings.config(for: kind)
        let level = settings.features.resolve(cfg.enforcement)
        let session = BreakSession(
            kind: kind, level: level, startedAt: date,
            endsAt: date.addingTimeInterval(TimeInterval(cfg.durationSeconds)),
            canPostpone: postponesUsed[kind, default: 0] < cfg.maxPostpones && level != .lock,
            skipAvailableAt: level == .lock ? nil : date.addingTimeInterval(TimeInterval(cfg.skipDelaySeconds)))
        phase = .onBreak(session)
        presenter?.show(session)
        if level == .notification { notifier?.notifyBreak(kind, seconds: cfg.durationSeconds) }
        if settings.features.sounds { notifier?.playStart() }
    }

    func postpone() {
        guard case .onBreak(let s) = phase, s.canPostpone, postponesLeft > 0 else { return }
        let cfg = settings.config(for: s.kind)
        postponesUsed[s.kind, default: 0] += 1
        let setBack = TimeInterval(max(0, (cfg.intervalMinutes - cfg.postponeMinutes) * 60))
        if s.kind == .micro { microElapsed = setBack } else { longElapsed = setBack }
        stats?.record(s.kind, .postponed, at: now)
        presenter?.hide()
        phase = .working
    }

    func skip() {
        guard case .onBreak(let s) = phase, let at = s.skipAvailableAt, now >= at else { return }
        finish(.skipped)
    }

    /// Emergency exit, also used for hard lock. Logged as skipped.
    func forceEnd() {
        guard case .onBreak = phase else { return }
        finish(.skipped)
    }

    func pause(minutes: Int?) {
        if case .onBreak = phase { finish(.skipped) }
        phase = .paused(until: minutes.map { now.addingTimeInterval(TimeInterval($0 * 60)) })
    }

    func pauseUntilTomorrow() {
        if case .onBreak = phase { finish(.skipped) }
        phase = .paused(until: StatsLog.dayStart(for: now).addingTimeInterval(24 * 3600))
    }

    func resume() {
        if case .paused = phase { phase = .working }
    }

    func resetTimers() {
        microElapsed = 0
        longElapsed = 0
    }

    private func finish(_ outcome: BreakOutcome) {
        guard case .onBreak(let s) = phase else { return }
        presenter?.hide()
        if s.kind == .long { longElapsed = 0 }
        microElapsed = 0
        postponesUsed[s.kind] = 0
        stats?.record(s.kind, outcome, at: now)
        if outcome == .done, settings.features.sounds { notifier?.playEnd() }
        phase = .working
    }
}
