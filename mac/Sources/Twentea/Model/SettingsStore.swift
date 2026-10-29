import Foundation
import Combine

/// Owns the live settings, the active preset and the user's saved presets. Persists to settings.json.
@MainActor
final class SettingsStore: ObservableObject {
    private struct Snapshot: Codable {
        var activePresetID: String
        var settings: AppSettings
        var userPresets: [Preset]
        var isOn: Bool?
    }

    @Published var settings: AppSettings { didSet { save() } }
    @Published private(set) var activePresetID: String
    @Published private(set) var userPresets: [Preset]
    @Published private(set) var isFirstLaunch: Bool
    /// Master power switch. Kept outside presets so switching presets never turns the app on or off.
    @Published var isOn: Bool { didSet { save() } }

    private let fileURL: URL

    init(directory: URL = SettingsStore.defaultDirectory) {
        fileURL = directory.appendingPathComponent("settings.json")
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)

        if let data = try? Data(contentsOf: fileURL),
           let snap = try? JSONDecoder().decode(Snapshot.self, from: data) {
            settings = snap.settings
            activePresetID = snap.activePresetID
            userPresets = snap.userPresets
            isOn = snap.isOn ?? true
            isFirstLaunch = false
        } else {
            let preset = Preset.builtIns.first { $0.id == Preset.defaultID }!
            settings = preset.settings
            activePresetID = preset.id
            userPresets = []
            isOn = true
            isFirstLaunch = true
            save()
        }
    }

    nonisolated static var defaultDirectory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Twentea", isDirectory: true)
    }

    var allPresets: [Preset] { Preset.builtIns + userPresets }

    var activePreset: Preset? { allPresets.first { $0.id == activePresetID } }

    /// Settings differ from the preset they came from.
    var isCustom: Bool { activePreset?.settings != settings }

    var activeTitle: String {
        let name = activePreset?.name ?? "Custom"
        return isCustom ? "\(name) · Custom" : name
    }

    func apply(presetID: String) {
        guard let preset = allPresets.first(where: { $0.id == presetID }) else { return }
        activePresetID = preset.id
        settings = preset.settings
    }

    func resetToPreset() {
        if let preset = activePreset { settings = preset.settings }
    }

    @discardableResult
    func saveAsPreset(name: String, symbol: String = "star") -> Preset {
        let preset = Preset(id: "user-\(UUID().uuidString.prefix(8).lowercased())", name: name,
                            symbol: symbol, builtIn: false, settings: settings)
        userPresets.append(preset)
        activePresetID = preset.id
        save()
        return preset
    }

    /// Overwrites the active user preset with the current settings.
    func updateActivePreset() {
        guard let i = userPresets.firstIndex(where: { $0.id == activePresetID }) else { return }
        userPresets[i].settings = settings
        save()
    }

    func renamePreset(id: String, to name: String) {
        guard let i = userPresets.firstIndex(where: { $0.id == id }) else { return }
        userPresets[i].name = name
        save()
    }

    func duplicatePreset(id: String) {
        guard let source = allPresets.first(where: { $0.id == id }) else { return }
        var copy = source
        copy.id = "user-\(UUID().uuidString.prefix(8).lowercased())"
        copy.name = "\(source.name) copy"
        copy.builtIn = false
        userPresets.append(copy)
        save()
    }

    func deletePreset(id: String) {
        userPresets.removeAll { $0.id == id }
        if activePresetID == id { activePresetID = Preset.defaultID }
        save()
    }

    func exportPreset(id: String) throws -> Data {
        guard let preset = allPresets.first(where: { $0.id == id }) else { throw CocoaError(.fileNoSuchFile) }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try encoder.encode(preset)
    }

    func importPreset(from data: Data) throws {
        var preset = try JSONDecoder().decode(Preset.self, from: data)
        preset.id = "user-\(UUID().uuidString.prefix(8).lowercased())"
        preset.builtIn = false
        userPresets.append(preset)
        save()
    }

    func markLaunched() { isFirstLaunch = false }

    private func save() {
        let snap = Snapshot(activePresetID: activePresetID, settings: settings, userPresets: userPresets, isOn: isOn)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? encoder.encode(snap) {
            try? data.write(to: fileURL, options: .atomic)
        }
    }
}
