import Foundation

enum BreakOutcome: String, Codable {
    case done, skipped, postponed, credited
}

struct BreakEvent: Codable {
    var date: Date
    var kind: BreakKind
    var outcome: BreakOutcome
}

/// Append-only break history in history.jsonl, plus today's counts for the menu.
@MainActor
final class StatsLog: ObservableObject {
    @Published private(set) var today: [BreakOutcome: Int] = [:]

    var enabled = true
    private let fileURL: URL
    private var dayStart: Date

    init(directory: URL = SettingsStore.defaultDirectory) {
        fileURL = directory.appendingPathComponent("history.jsonl")
        dayStart = Self.dayStart(for: Date())
        loadToday()
    }

    /// Stats days start at 04:00 so a late session counts toward the day it began.
    static func dayStart(for date: Date, calendar: Calendar = .current) -> Date {
        let shifted = date.addingTimeInterval(-4 * 3600)
        return calendar.startOfDay(for: shifted).addingTimeInterval(4 * 3600)
    }

    var adherence: Int? {
        let taken = (today[.done] ?? 0) + (today[.credited] ?? 0)
        let total = taken + (today[.skipped] ?? 0)
        return total == 0 ? nil : Int((Double(taken) / Double(total) * 100).rounded())
    }

    func record(_ kind: BreakKind, _ outcome: BreakOutcome, at date: Date = Date()) {
        guard enabled else { return }
        rollDayIfNeeded(date)
        today[outcome, default: 0] += 1

        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        guard var line = try? encoder.encode(BreakEvent(date: date, kind: kind, outcome: outcome)) else { return }
        line.append(0x0A)
        if let handle = try? FileHandle(forWritingTo: fileURL) {
            handle.seekToEndOfFile()
            handle.write(line)
            try? handle.close()
        } else {
            try? line.write(to: fileURL)
        }
    }

    private func rollDayIfNeeded(_ date: Date) {
        let start = Self.dayStart(for: date)
        if start != dayStart {
            dayStart = start
            today = [:]
        }
    }

    private func loadToday() {
        guard let text = try? String(contentsOf: fileURL, encoding: .utf8) else { return }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        for line in text.split(separator: "\n") {
            if let event = try? decoder.decode(BreakEvent.self, from: Data(line.utf8)), event.date >= dayStart {
                today[event.outcome, default: 0] += 1
            }
        }
    }
}
