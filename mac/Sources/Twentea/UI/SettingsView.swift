import AppKit
import SwiftUI
import UniformTypeIdentifiers

enum SettingsSection: String, CaseIterable, Identifiable {
    case presets = "Presets"
    case schedule = "Schedule"
    case breaks = "Breaks"
    case surfaces = "Features"
    case smartPause = "Smart pause"
    case general = "General"

    var id: String { rawValue }

    var symbol: String {
        switch self {
        case .presets: "square.stack"
        case .schedule: "calendar"
        case .breaks: "cup.and.saucer"
        case .surfaces: "switch.2"
        case .smartPause: "pause.circle"
        case .general: "gearshape"
        }
    }
}

struct SettingsView: View {
    let app: AppModel
    @ObservedObject var store: SettingsStore
    @ObservedObject var scheduler: Scheduler
    @StateObject private var section = ViewState(SettingsSection.presets)

    var body: some View {
        NavigationSplitView {
            List(SettingsSection.allCases, selection: $section.value) { s in
                Label(s.rawValue, systemImage: s.symbol).tag(s)
            }
            .navigationSplitViewColumnWidth(170)
        } detail: {
            VStack(spacing: 0) {
                PresetBar(store: store)
                Divider()
                Form { detail }
                    .formStyle(.grouped)
            }
        }
        .frame(minWidth: 700, minHeight: 480)
    }

    @ViewBuilder
    private var detail: some View {
        switch section.value {
        case .presets: PresetsSection(store: store)
        case .schedule: ScheduleSection(schedule: $store.settings.schedule)
        case .breaks: BreaksSection(settings: $store.settings, scheduler: scheduler)
        case .surfaces: FeaturesSection(features: $store.settings.features)
        case .smartPause: SmartPauseSection(enabled: $store.settings.features.smartPause, rules: $store.settings.smartPause)
        case .general: GeneralSection(app: app, settings: $store.settings, scheduler: scheduler)
        }
    }
}

// MARK: - Preset bar

private struct PresetBar: View {
    @ObservedObject var store: SettingsStore
    @StateObject private var naming = ViewState(false)
    @StateObject private var name = ViewState("")

    var body: some View {
        HStack(spacing: 10) {
            PowerButton(store: store, size: 30)
            Divider().frame(height: 22)
            Image(systemName: store.activePreset?.symbol ?? "star")
            Text(store.activeTitle).font(.headline)
            if store.isCustom {
                Text("Changed").font(.caption).padding(.horizontal, 6).padding(.vertical, 2)
                    .background(Capsule().fill(Color.orange.opacity(0.2)))
            }
            Spacer()
            if store.isCustom {
                Button("Reset") { store.resetToPreset() }
                if store.activePreset?.builtIn == false {
                    Button("Update preset") { store.updateActivePreset() }
                }
            }
            Button("Save as preset…") { name.value = ""; naming.value = true }
        }
        .padding(.horizontal, 16).padding(.vertical, 10)
        .alert("Save current settings as a preset", isPresented: $naming.value) {
            TextField("Name", text: $name.value)
            Button("Save") { if !name.value.isEmpty { store.saveAsPreset(name: name.value) } }
            Button("Cancel", role: .cancel) {}
        }
    }
}

// MARK: - Presets

private struct PresetsSection: View {
    @ObservedObject var store: SettingsStore
    @StateObject private var renaming = ViewState<Preset?>(nil)
    @StateObject private var newName = ViewState("")

    var body: some View {
        Section("Built-in") {
            ForEach(Preset.builtIns) { row($0) }
        }
        Section("My presets") {
            if store.userPresets.isEmpty {
                Text("Change any setting, then use Save as preset.").foregroundStyle(.secondary)
            }
            ForEach(store.userPresets) { row($0) }
        }
        Section {
            HStack {
                Button("Import preset…", action: importPreset)
                Spacer()
            }
        }
        .alert("Rename preset", isPresented: Binding(get: { renaming.value != nil }, set: { if !$0 { renaming.value = nil } })) {
            TextField("Name", text: $newName.value)
            Button("Rename") { if let p = renaming.value { store.renamePreset(id: p.id, to: newName.value) } }
            Button("Cancel", role: .cancel) {}
        }
    }

    private func row(_ preset: Preset) -> some View {
        HStack {
            Image(systemName: preset.symbol).frame(width: 20)
            VStack(alignment: .leading) {
                Text(preset.name)
                Text(summary(preset.settings)).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if preset.id == store.activePresetID {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(.orange)
            } else {
                Button("Use") { store.apply(presetID: preset.id) }
            }
            Menu {
                Button("Duplicate") { store.duplicatePreset(id: preset.id) }
                Button("Export…") { export(preset) }
                if !preset.builtIn {
                    Button("Rename…") { newName.value = preset.name; renaming.value = preset }
                    Button("Delete", role: .destructive) { store.deletePreset(id: preset.id) }
                }
            } label: { Image(systemName: "ellipsis.circle") }
                .menuStyle(.borderlessButton).fixedSize()
        }
    }

    private func summary(_ s: AppSettings) -> String {
        var parts = ["Micro \(s.micro.intervalMinutes)m · \(s.micro.enforcement.title)"]
        if s.long.enabled { parts.append("Long \(s.long.intervalMinutes)m · \(s.long.enforcement.title)") }
        return parts.joined(separator: "  ·  ")
    }

    private func export(_ preset: Preset) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "\(preset.name).twentea"
        guard panel.runModal() == .OK, let url = panel.url,
              let data = try? store.exportPreset(id: preset.id) else { return }
        try? data.write(to: url)
    }

    private func importPreset() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "twentea") ?? .json, .json]
        guard panel.runModal() == .OK, let url = panel.url, let data = try? Data(contentsOf: url) else { return }
        try? store.importPreset(from: data)
    }
}

// MARK: - Schedule

private struct ScheduleSection: View {
    @Binding var schedule: WeekSchedule

    var body: some View {
        Section {
            Toggle("Only run during working hours", isOn: $schedule.enabled)
        } footer: {
            Text("A window that ends at or before its start runs past midnight, for example 14:00 to 00:00.")
        }
        if schedule.enabled {
            Section("Week") {
                ForEach(orderedIndices, id: \.self) { i in
                    DayRow(day: $schedule.days[i])
                }
            }
            Section {
                Button("Copy Monday to all weekdays") {
                    guard let monday = schedule.days.first(where: { $0.weekday == 2 }) else { return }
                    for i in schedule.days.indices where (2...6).contains(schedule.days[i].weekday) {
                        schedule.days[i].windows = monday.windows
                        schedule.days[i].enabled = monday.enabled
                    }
                }
            }
        }
    }

    /// Monday first.
    private var orderedIndices: [Int] {
        [2, 3, 4, 5, 6, 7, 1].compactMap { wd in schedule.days.firstIndex { $0.weekday == wd } }
    }
}

private struct DayRow: View {
    @Binding var day: DaySchedule

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Toggle(Calendar.current.weekdaySymbols[day.weekday - 1], isOn: $day.enabled)
            if day.enabled {
                ForEach($day.windows) { $window in
                    HStack {
                        DatePicker("From", selection: minuteBinding($window.startMinute), displayedComponents: .hourAndMinute)
                        DatePicker("to", selection: minuteBinding($window.endMinute), displayedComponents: .hourAndMinute)
                        if window.crossesMidnight { Text("+1 day").font(.caption).foregroundStyle(.secondary) }
                        Spacer()
                        if day.windows.count > 1 {
                            Button { day.windows.removeAll { $0.id == window.id } } label: { Image(systemName: "minus.circle") }
                                .buttonStyle(.borderless)
                        }
                    }
                    .labelsHidden()
                }
                if day.windows.count < 4 {
                    Button("Add window") { day.windows.append(TimeWindow(startMinute: 9 * 60, endMinute: 17 * 60)) }
                        .buttonStyle(.borderless).font(.caption)
                }
            }
        }
    }

    private func minuteBinding(_ minute: Binding<Int>) -> Binding<Date> {
        Binding(
            get: { Calendar.current.startOfDay(for: Date()).addingTimeInterval(TimeInterval(minute.wrappedValue * 60)) },
            set: {
                let c = Calendar.current.dateComponents([.hour, .minute], from: $0)
                minute.wrappedValue = (c.hour ?? 0) * 60 + (c.minute ?? 0)
            })
    }
}

// MARK: - Breaks

private struct BreaksSection: View {
    @Binding var settings: AppSettings
    let scheduler: Scheduler

    var body: some View {
        BreakEditor(kind: .micro, config: $settings.micro, features: settings.features, scheduler: scheduler,
                    intervals: [5, 10, 15, 20, 25, 30, 40, 45, 60], durations: [10, 15, 20, 30, 40, 60, 90])
        BreakEditor(kind: .long, config: $settings.long, features: settings.features, scheduler: scheduler,
                    intervals: [30, 40, 45, 50, 60, 75, 90, 120], durations: [60, 120, 180, 300, 600, 900])
        Section {
            Picker("Wait for a natural pause up to", selection: $settings.breakpointGraceSeconds) {
                ForEach([0, 60, 120, 180, 300], id: \.self) { Text($0 == 0 ? "Don't wait" : "\($0 / 60) min").tag($0) }
            }
        } footer: {
            Text("When a break is due, Twentea waits until you stop typing or switch apps, up to this long.")
        }
    }
}

private struct BreakEditor: View {
    let kind: BreakKind
    @Binding var config: BreakConfig
    let features: Features
    let scheduler: Scheduler
    let intervals: [Int]
    let durations: [Int]

    var body: some View {
        Section(kind.title) {
            Toggle("Enabled", isOn: $config.enabled)
            if config.enabled {
                Picker("Every", selection: $config.intervalMinutes) {
                    ForEach(withCurrent(intervals, config.intervalMinutes), id: \.self) { Text("\($0) min").tag($0) }
                }
                Picker("For", selection: $config.durationSeconds) {
                    ForEach(withCurrent(durations, config.durationSeconds), id: \.self) { Text(Self.duration($0)).tag($0) }
                }
                Picker("Shows as", selection: $config.enforcement) {
                    ForEach(Enforcement.allCases) { level in
                        Text(features.allows(level) ? level.title : "\(level.title) (switched off)").tag(level)
                    }
                }
                if features.resolve(config.enforcement) != config.enforcement {
                    Text("Falls back to \(features.resolve(config.enforcement).title) because a feature is switched off.")
                        .font(.caption).foregroundStyle(.orange)
                }
                Stepper("Postpones per break: \(config.maxPostpones >= 99 ? "unlimited" : "\(config.maxPostpones)")",
                        value: $config.maxPostpones, in: 0...99)
                Picker("Postpone by", selection: $config.postponeMinutes) {
                    ForEach(withCurrent([1, 2, 5, 10, 15, 30], config.postponeMinutes), id: \.self) { Text("\($0) min").tag($0) }
                }
                Picker("Skip button after", selection: $config.skipDelaySeconds) {
                    ForEach(withCurrent([0, 3, 5, 10, 30], config.skipDelaySeconds), id: \.self) {
                        Text($0 == 0 ? "Immediately" : "\($0) s").tag($0)
                    }
                }
                Button("Preview \(kind.title.lowercased())") { scheduler.startBreak(kind) }
            }
        }
    }

    private func withCurrent(_ options: [Int], _ current: Int) -> [Int] {
        options.contains(current) ? options : (options + [current]).sorted()
    }

    static func duration(_ seconds: Int) -> String {
        seconds < 60 ? "\(seconds) s" : (seconds % 60 == 0 ? "\(seconds / 60) min" : "\(seconds / 60) min \(seconds % 60) s")
    }
}

// MARK: - Features

private struct FeaturesSection: View {
    @Binding var features: Features

    var body: some View {
        Section("How breaks reach you") {
            toggle("Screen dimming", "Breaks use the floating card or a notification instead.", $features.screenDimming)
            toggle("Full-screen overlay", "Soft dim becomes the strongest level.", $features.fullOverlay)
            toggle("Hard lock", "Overlays always keep a Skip button.", $features.hardLock)
            toggle("Floating card", "Breaks fall back to notifications.", $features.floatingCard)
            toggle("Notifications", "Breaks show only in the menu bar.", $features.notifications)
            toggle("Sounds", "Visual cues only.", $features.sounds)
        }
        Section("Menu bar") {
            toggle("Countdown in menu bar", "Icon only.", $features.menuBarCountdown)
        }
        Section("Timing") {
            toggle("Count time away as a break", "Breaks follow the timer even after you step away.", $features.idleCredit)
            toggle("Smart pause", "Breaks fire during calls and videos too.", $features.smartPause)
            toggle("Restart a dimmed break when you move the mouse or type",
                   "The countdown keeps running whatever you do.", $features.restartOnInput)
        }
    }

    private func toggle(_ title: String, _ off: String, _ value: Binding<Bool>) -> some View {
        Toggle(isOn: value) {
            VStack(alignment: .leading) {
                Text(title)
                Text("Off: \(off)").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

// MARK: - Smart pause

private struct SmartPauseSection: View {
    @Binding var enabled: Bool
    @Binding var rules: SmartPauseRules

    var body: some View {
        Section {
            Toggle("Smart pause", isOn: $enabled)
        } footer: {
            Text("The timer stops while any rule below matches. No permissions needed.")
        }
        if enabled {
            Section("Rules") {
                Toggle("Camera or microphone in use", isOn: $rules.cameraOrMic)
                Toggle("Video playing or screen kept awake", isOn: $rules.videoPlaying)
            }
            Section("Apps") {
                ForEach(rules.excludedBundleIDs, id: \.self) { id in
                    HStack {
                        Text(appName(id))
                        Spacer()
                        Button { rules.excludedBundleIDs.removeAll { $0 == id } } label: { Image(systemName: "minus.circle") }
                            .buttonStyle(.borderless)
                    }
                }
                Button("Add app…", action: addApp)
            }
        }
    }

    private func appName(_ bundleID: String) -> String {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) else { return bundleID }
        return FileManager.default.displayName(atPath: url.path)
    }

    private func addApp() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.application]
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        guard panel.runModal() == .OK, let url = panel.url,
              let id = Bundle(url: url)?.bundleIdentifier, !rules.excludedBundleIDs.contains(id) else { return }
        rules.excludedBundleIDs.append(id)
    }
}

// MARK: - General

private struct GeneralSection: View {
    @ObservedObject var app: AppModel
    @Binding var settings: AppSettings
    let scheduler: Scheduler

    var body: some View {
        Section {
            Toggle("Open at login", isOn: Binding(get: { app.launchAtLogin }, set: { app.launchAtLogin = $0 }))
        }
        Section("Data") {
            Button("Reset break timers") { scheduler.resetTimers() }
            Button("Show data folder") { NSWorkspace.shared.open(SettingsStore.defaultDirectory) }
        }
        Section("Automation") {
            LabeledContent("Start break", value: "twentea://break")
            LabeledContent("Pause", value: "twentea://pause?minutes=30")
            LabeledContent("Switch preset", value: "twentea://preset?id=night-owl")
        }
        Section {
            LabeledContent("Twentea", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev")
        }
    }
}

/// Local view state. Stands in for @State, whose macro plugin ships only with full Xcode.
final class ViewState<Value>: ObservableObject {
    @Published var value: Value
    init(_ value: Value) { self.value = value }
}
