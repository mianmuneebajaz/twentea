import Foundation

struct Preset: Codable, Identifiable, Equatable {
    var id: String
    var name: String
    var symbol: String
    var builtIn: Bool
    var settings: AppSettings
}

extension Preset {
    private static let weekdays: Set<Int> = [2, 3, 4, 5, 6]

    private static func breaks(
        micro: (Int, Int, Enforcement), long: (Int, Int, Enforcement)?,
        postpones: Int, postponeMinutes: Int, skipDelay: Int = 0
    ) -> (BreakConfig, BreakConfig) {
        let m = BreakConfig(enabled: true, intervalMinutes: micro.0, durationSeconds: micro.1, enforcement: micro.2,
                            maxPostpones: postpones, postponeMinutes: postponeMinutes, skipDelaySeconds: skipDelay)
        let l = BreakConfig(enabled: long != nil, intervalMinutes: long?.0 ?? 60, durationSeconds: long?.1 ?? 300,
                            enforcement: long?.2 ?? .notification, maxPostpones: postpones,
                            postponeMinutes: postponeMinutes, skipDelaySeconds: skipDelay)
        return (m, l)
    }

    private static func features(_ ceiling: Enforcement, sounds: Bool = true, smartPause: Bool = true) -> Features {
        Features(notifications: true, sounds: sounds, floatingCard: true,
                 screenDimming: ceiling >= .softDim, fullOverlay: ceiling >= .overlay, hardLock: ceiling >= .lock,
                 menuBarCountdown: true, idleCredit: true, smartPause: smartPause)
    }

    private static let allPause = SmartPauseRules(cameraOrMic: true, videoPlaying: true, excludedBundleIDs: [])

    private static func make(_ id: String, _ name: String, _ symbol: String,
                             _ b: (BreakConfig, BreakConfig), _ schedule: WeekSchedule,
                             _ f: Features, _ pause: SmartPauseRules = allPause) -> Preset {
        Preset(id: id, name: name, symbol: symbol, builtIn: true,
               settings: AppSettings(micro: b.0, long: b.1, schedule: schedule, features: f,
                                     smartPause: pause, breakpointGraceSeconds: 180))
    }

    static let builtIns: [Preset] = [
        make("gentle", "Gentle", "leaf",
             breaks(micro: (30, 20, .notification), long: (90, 300, .notification), postpones: 99, postponeMinutes: 5),
             .uniform(weekdays: weekdays, TimeWindow(startMinute: 9 * 60, endMinute: 17 * 60)),
             features(.card)),
        make("balanced", "Balanced", "cup.and.saucer",
             breaks(micro: (20, 20, .softDim), long: (60, 300, .overlay), postpones: 2, postponeMinutes: 5),
             .uniform(weekdays: weekdays, TimeWindow(startMinute: 9 * 60, endMinute: 18 * 60)),
             features(.overlay)),
        make("strict", "Strict", "lock",
             breaks(micro: (20, 20, .overlay), long: (50, 600, .lock), postpones: 1, postponeMinutes: 2, skipDelay: 5),
             .uniform(weekdays: weekdays, TimeWindow(startMinute: 9 * 60, endMinute: 18 * 60)),
             features(.lock),
             SmartPauseRules(cameraOrMic: false, videoPlaying: true, excludedBundleIDs: [])),
        make("night-owl", "Night owl", "moon.stars",
             breaks(micro: (20, 20, .softDim), long: (60, 300, .overlay), postpones: 2, postponeMinutes: 5),
             .uniform(weekdays: weekdays, TimeWindow(startMinute: 14 * 60, endMinute: 0)),
             features(.overlay)),
        make("meeting-heavy", "Meeting heavy", "person.2",
             breaks(micro: (25, 20, .card), long: (90, 300, .softDim), postpones: 3, postponeMinutes: 10),
             .uniform(weekdays: weekdays, TimeWindow(startMinute: 8 * 60 + 30, endMinute: 18 * 60)),
             features(.softDim, sounds: false)),
        make("minimal", "Minimal", "circle",
             breaks(micro: (20, 20, .menuBar), long: nil, postpones: 99, postponeMinutes: 5),
             .always,
             features(.menuBar)),
    ]

    static let defaultID = "balanced"
}
