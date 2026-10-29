import Foundation

/// How strongly a break interrupts the user, weakest first.
enum Enforcement: String, Codable, CaseIterable, Identifiable, Comparable {
    case menuBar, notification, card, softDim, overlay, lock

    var id: String { rawValue }

    var title: String {
        switch self {
        case .menuBar: "Menu bar only"
        case .notification: "Notification"
        case .card: "Floating card"
        case .softDim: "Soft dim"
        case .overlay: "Full overlay"
        case .lock: "Hard lock"
        }
    }

    private var rank: Int { Self.allCases.firstIndex(of: self)! }
    static func < (lhs: Self, rhs: Self) -> Bool { lhs.rank < rhs.rank }
}

enum BreakKind: String, Codable, CaseIterable {
    case micro, long

    var title: String { self == .micro ? "Micro break" : "Long break" }
}

struct BreakConfig: Codable, Equatable {
    var enabled: Bool
    var intervalMinutes: Int
    var durationSeconds: Int
    var enforcement: Enforcement
    var maxPostpones: Int
    var postponeMinutes: Int
    var skipDelaySeconds: Int
}

/// A working window in minutes after midnight. `endMinute <= startMinute` crosses midnight.
struct TimeWindow: Codable, Equatable, Hashable, Identifiable {
    var id = UUID()
    var startMinute: Int
    var endMinute: Int

    var crossesMidnight: Bool { endMinute <= startMinute }

    enum CodingKeys: String, CodingKey { case startMinute, endMinute }

    init(startMinute: Int, endMinute: Int) {
        self.startMinute = startMinute
        self.endMinute = endMinute
    }

    static func == (lhs: Self, rhs: Self) -> Bool {
        lhs.startMinute == rhs.startMinute && lhs.endMinute == rhs.endMinute
    }

    func hash(into hasher: inout Hasher) {
        hasher.combine(startMinute)
        hasher.combine(endMinute)
    }
}

struct DaySchedule: Codable, Equatable {
    /// Calendar weekday, 1 = Sunday ... 7 = Saturday.
    var weekday: Int
    var enabled: Bool
    var windows: [TimeWindow]
}

struct WeekSchedule: Codable, Equatable {
    var enabled: Bool
    var days: [DaySchedule]

    static func uniform(weekdays: Set<Int>, _ window: TimeWindow) -> WeekSchedule {
        WeekSchedule(enabled: true, days: (1...7).map {
            DaySchedule(weekday: $0, enabled: weekdays.contains($0), windows: [window])
        })
    }

    static let always = WeekSchedule(enabled: false, days: (1...7).map {
        DaySchedule(weekday: $0, enabled: true, windows: [TimeWindow(startMinute: 0, endMinute: 0)])
    })

    /// True when `date` falls inside an enabled window, including windows that started yesterday and cross midnight.
    func isActive(at date: Date, calendar: Calendar = .current) -> Bool {
        guard enabled else { return true }
        let comps = calendar.dateComponents([.weekday, .hour, .minute], from: date)
        guard let weekday = comps.weekday else { return true }
        let minute = (comps.hour ?? 0) * 60 + (comps.minute ?? 0)
        let yesterday = weekday == 1 ? 7 : weekday - 1

        for day in days where day.enabled {
            for w in day.windows {
                if day.weekday == weekday {
                    if w.crossesMidnight ? minute >= w.startMinute : (minute >= w.startMinute && minute < w.endMinute) {
                        return true
                    }
                } else if day.weekday == yesterday, w.crossesMidnight, minute < w.endMinute {
                    return true
                }
            }
        }
        return false
    }
}

/// Master switches. Turning one off degrades breaks to the next weaker surface.
struct Features: Codable, Equatable {
    var notifications: Bool
    var sounds: Bool
    var floatingCard: Bool
    var screenDimming: Bool
    var fullOverlay: Bool
    var hardLock: Bool
    var menuBarCountdown: Bool
    var idleCredit: Bool
    var smartPause: Bool
    /// During a dim or overlay, input restarts the countdown so the break means looking away.
    var restartOnInput: Bool = true

    enum CodingKeys: String, CodingKey {
        case notifications, sounds, floatingCard, screenDimming, fullOverlay, hardLock
        case menuBarCountdown, idleCredit, smartPause, restartOnInput
    }

    init(notifications: Bool, sounds: Bool, floatingCard: Bool, screenDimming: Bool, fullOverlay: Bool,
         hardLock: Bool, menuBarCountdown: Bool, idleCredit: Bool, smartPause: Bool, restartOnInput: Bool = true) {
        self.notifications = notifications
        self.sounds = sounds
        self.floatingCard = floatingCard
        self.screenDimming = screenDimming
        self.fullOverlay = fullOverlay
        self.hardLock = hardLock
        self.menuBarCountdown = menuBarCountdown
        self.idleCredit = idleCredit
        self.smartPause = smartPause
        self.restartOnInput = restartOnInput
    }

    /// Settings files written before a switch existed load with that switch's default.
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        notifications = try c.decode(Bool.self, forKey: .notifications)
        sounds = try c.decode(Bool.self, forKey: .sounds)
        floatingCard = try c.decode(Bool.self, forKey: .floatingCard)
        screenDimming = try c.decode(Bool.self, forKey: .screenDimming)
        fullOverlay = try c.decode(Bool.self, forKey: .fullOverlay)
        hardLock = try c.decode(Bool.self, forKey: .hardLock)
        menuBarCountdown = try c.decode(Bool.self, forKey: .menuBarCountdown)
        idleCredit = try c.decode(Bool.self, forKey: .idleCredit)
        smartPause = try c.decode(Bool.self, forKey: .smartPause)
        restartOnInput = try c.decodeIfPresent(Bool.self, forKey: .restartOnInput) ?? true
    }

    func allows(_ level: Enforcement) -> Bool {
        switch level {
        case .menuBar: true
        case .notification: notifications
        case .card: floatingCard
        case .softDim: screenDimming
        case .overlay: fullOverlay
        case .lock: hardLock
        }
    }

    /// Strongest allowed level at or below `requested`.
    func resolve(_ requested: Enforcement) -> Enforcement {
        Enforcement.allCases.reversed().first { $0 <= requested && allows($0) } ?? .menuBar
    }
}

struct SmartPauseRules: Codable, Equatable {
    var cameraOrMic: Bool
    var videoPlaying: Bool
    var excludedBundleIDs: [String]
}

struct AppSettings: Codable, Equatable {
    var micro: BreakConfig
    var long: BreakConfig
    var schedule: WeekSchedule
    var features: Features
    var smartPause: SmartPauseRules
    var breakpointGraceSeconds: Int

    func config(for kind: BreakKind) -> BreakConfig {
        kind == .micro ? micro : long
    }
}
