import Foundation
import Testing
@testable import Twentea

/// shared/presets.json is the contract both apps follow. The Windows app loads it directly;
/// this test keeps the Swift built-ins identical to it.
/// Run with TWENTEA_WRITE_PRESETS=1 to regenerate the file after changing a built-in preset.
struct SharedPresetsTests {
    static let fileURL = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .deletingLastPathComponent().appendingPathComponent("shared/presets.json")

    @Test func builtInPresetsMatchSharedFile() throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        if ProcessInfo.processInfo.environment["TWENTEA_WRITE_PRESETS"] == "1" {
            try encoder.encode(Preset.builtIns).write(to: Self.fileURL, options: .atomic)
        }
        let shared = try JSONDecoder().decode([Preset].self, from: Data(contentsOf: Self.fileURL))
        #expect(shared == Preset.builtIns)
    }
}
