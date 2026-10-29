import Foundation
import Testing
@testable import Twentea

@MainActor
final class FakeActivity: ActivitySource {
    var idleSeconds: TimeInterval = 0
    var lastAppSwitch = Date.distantPast
    var pauseReason: String?
    func smartPauseReason(_ rules: SmartPauseRules) -> String? { pauseReason }
}

@MainActor
final class FakePresenter: BreakPresenting {
    var shown: [BreakSession] = []
    var hidden = 0
    func show(_ session: BreakSession) { shown.append(session) }
    func update(_ session: BreakSession) {}
    func hide() { hidden += 1 }
}

@MainActor
struct SchedulerTests {
    let start = Date(timeIntervalSince1970: 1_800_000_000)

    func settings(_ id: String = "balanced") -> AppSettings {
        var s = Preset.builtIns.first { $0.id == id }!.settings
        s.schedule = .always
        s.features.smartPause = true
        return s
    }

    /// Ticks once a second for `seconds`.
    func run(_ sched: Scheduler, from: Date, seconds: Int) -> Date {
        var t = from
        for _ in 0..<seconds { t += 1; sched.tick(t) }
        return t
    }

    @Test func microBreakStartsAfterIntervalAtBreakpoint() {
        let activity = FakeActivity()
        let presenter = FakePresenter()
        let sched = Scheduler(settings: settings(), activity: activity, presenter: presenter)
        sched.tick(start)
        var t = run(sched, from: start, seconds: 20 * 60)
        guard case .due(.micro, _) = sched.phase else { Issue.record("expected due, got \(sched.phase)"); return }

        activity.idleSeconds = 4  // user paused typing
        t = run(sched, from: t, seconds: 1)
        #expect(presenter.shown.first?.kind == .micro)
        #expect(presenter.shown.first?.level == .softDim)

        activity.idleSeconds = 30
        _ = run(sched, from: t, seconds: 21)
        #expect(sched.phase == .working)
        #expect(sched.microElapsed < 5)
    }

    @Test func inputRestartsCountdownAndProgress() {
        let activity = FakeActivity()
        activity.idleSeconds = 30
        let sched = Scheduler(settings: settings(), activity: activity, presenter: FakePresenter())
        sched.tick(start)
        sched.startBreak(.micro, at: start)
        var t = run(sched, from: start, seconds: 10)
        activity.idleSeconds = 0  // mouse moved
        t = run(sched, from: t, seconds: 1)
        guard case .onBreak(let s) = sched.phase else { Issue.record("expected break"); return }
        #expect(s.startedAt == t)
        #expect(s.endsAt.timeIntervalSince(s.startedAt) == 20)

        var noRestart = settings()
        noRestart.features.restartOnInput = false
        let sched2 = Scheduler(settings: noRestart, activity: activity, presenter: FakePresenter())
        sched2.tick(start)
        sched2.startBreak(.micro, at: start)
        _ = run(sched2, from: start, seconds: 21)
        #expect(sched2.phase == .working)
    }

    @Test func oldSettingsWithoutNewSwitchStillLoad() throws {
        let json = #"{"notifications":true,"sounds":true,"floatingCard":true,"screenDimming":true,"fullOverlay":true,"hardLock":false,"menuBarCountdown":true,"idleCredit":true,"smartPause":true}"#
        let f = try JSONDecoder().decode(Features.self, from: Data(json.utf8))
        #expect(f.restartOnInput)
    }

    @Test func graceForcesBreakWhileTyping() {
        let activity = FakeActivity()
        let presenter = FakePresenter()
        let sched = Scheduler(settings: settings(), activity: activity, presenter: presenter)
        sched.tick(start)
        _ = run(sched, from: start, seconds: 20 * 60 + 181)
        #expect(presenter.shown.count == 1)
    }

    @Test func idleCreditResetsMicroTimer() {
        let activity = FakeActivity()
        let sched = Scheduler(settings: settings(), activity: activity, presenter: FakePresenter())
        sched.tick(start)
        var t = run(sched, from: start, seconds: 300)
        activity.idleSeconds = 130
        t = run(sched, from: t, seconds: 1)
        #expect(sched.microElapsed < 2)
        #expect(sched.longElapsed > 290)
    }

    @Test func readingPauseIsNotABreak() {
        let activity = FakeActivity()
        let sched = Scheduler(settings: settings(), activity: activity, presenter: FakePresenter())
        sched.tick(start)
        var t = run(sched, from: start, seconds: 300)
        activity.idleSeconds = 25  // reading, longer than the 20 s micro break
        t = run(sched, from: t, seconds: 1)
        #expect(sched.microElapsed > 290)
    }

    @Test func smartPauseStopsTimer() {
        let activity = FakeActivity()
        activity.pauseReason = "Paused during a call"
        let sched = Scheduler(settings: settings(), activity: activity, presenter: FakePresenter())
        sched.tick(start)
        _ = run(sched, from: start, seconds: 600)
        #expect(sched.microElapsed == 0)
        #expect(sched.phase == .smartPaused("Paused during a call"))
    }

    @Test func postponeSetsTimerBackAndRespectsLimit() {
        let activity = FakeActivity()
        let sched = Scheduler(settings: settings(), activity: activity, presenter: FakePresenter())
        sched.tick(start)
        sched.startBreak(.micro, at: start)
        sched.postpone()
        #expect(sched.microElapsed == TimeInterval((20 - 5) * 60))
        sched.startBreak(.micro, at: start)
        sched.postpone()
        sched.startBreak(.micro, at: start)
        guard case .onBreak(let s) = sched.phase else { Issue.record("expected break"); return }
        #expect(s.canPostpone == false)
    }

    @Test func powerOffStopsEverything() {
        let activity = FakeActivity()
        let presenter = FakePresenter()
        let sched = Scheduler(settings: settings(), activity: activity, presenter: presenter)
        sched.tick(start)
        sched.startBreak(.micro, at: start)
        sched.isOn = false
        #expect(sched.phase == .off)
        #expect(presenter.hidden == 1)
        _ = run(sched, from: start, seconds: 30 * 60)
        #expect(sched.microElapsed == 0)
        sched.startBreak(.micro, at: start)
        #expect(sched.phase == .off)
        sched.isOn = true
        #expect(sched.phase == .working)
    }

    @Test func switchedOffFeaturesDegradeEnforcement() {
        var f = settings().features
        f.fullOverlay = false
        f.screenDimming = false
        #expect(f.resolve(.overlay) == .card)
        f.floatingCard = false
        #expect(f.resolve(.lock) == .notification)
        f.notifications = false
        #expect(f.resolve(.lock) == .menuBar)
    }

    @Test func scheduleCrossesMidnight() {
        let schedule = WeekSchedule.uniform(weekdays: [2], TimeWindow(startMinute: 14 * 60, endMinute: 0))
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        // 2026-11-02 is a Monday.
        let mon1500 = cal.date(from: DateComponents(year: 2026, month: 11, day: 2, hour: 15))!
        let mon1300 = cal.date(from: DateComponents(year: 2026, month: 11, day: 2, hour: 13))!
        let tue0030 = cal.date(from: DateComponents(year: 2026, month: 11, day: 3, hour: 0, minute: 30))!
        #expect(schedule.isActive(at: mon1500, calendar: cal))
        #expect(!schedule.isActive(at: mon1300, calendar: cal))
        #expect(!schedule.isActive(at: tue0030, calendar: cal))

        let late = WeekSchedule.uniform(weekdays: [2], TimeWindow(startMinute: 14 * 60, endMinute: 2 * 60))
        #expect(late.isActive(at: tue0030, calendar: cal))
    }

    @Test func storeDetectsCustomAndSavesPresets() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let store = SettingsStore(directory: dir)
        #expect(store.isFirstLaunch)
        #expect(!store.isCustom)
        store.settings.micro.intervalMinutes = 25
        #expect(store.isCustom)
        let saved = store.saveAsPreset(name: "Mine")
        #expect(!store.isCustom)
        #expect(store.activePresetID == saved.id)

        let reopened = SettingsStore(directory: dir)
        #expect(reopened.userPresets.map(\.name) == ["Mine"])
        #expect(reopened.settings.micro.intervalMinutes == 25)

        let data = try reopened.exportPreset(id: saved.id)
        try reopened.importPreset(from: data)
        #expect(reopened.userPresets.count == 2)
    }
}
