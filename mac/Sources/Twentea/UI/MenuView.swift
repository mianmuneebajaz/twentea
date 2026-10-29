import SwiftUI

struct MenuBarLabel: View {
    @ObservedObject var scheduler: Scheduler
    @ObservedObject var store: SettingsStore

    var body: some View {
        HStack(spacing: 4) {
            if case .off = scheduler.phase {
                Image(nsImage: Self.slashedCup)
            } else {
                Image(systemName: icon)
            }
            if store.isOn, store.settings.features.menuBarCountdown, let text { Text(text).monospacedDigit() }
        }
    }

    private var icon: String {
        switch scheduler.phase {
        case .off, .paused, .smartPaused, .offHours: "cup.and.saucer"
        case .onBreak: "eye"
        default: "cup.and.saucer.fill"
        }
    }

    /// The cup with a diagonal line through it, drawn as a template image so the menu bar tints it.
    static let slashedCup: NSImage = {
        let config = NSImage.SymbolConfiguration(pointSize: 14, weight: .regular)
        guard let cup = NSImage(systemSymbolName: "cup.and.saucer", accessibilityDescription: nil)?
            .withSymbolConfiguration(config) else { return NSImage() }
        let image = NSImage(size: cup.size, flipped: false) { rect in
            cup.draw(in: rect)
            let from = NSPoint(x: rect.minX + 1, y: rect.maxY - 1)
            let to = NSPoint(x: rect.maxX - 1, y: rect.minY + 1)
            // Clear a gap around the line so it reads against the cup, then draw the line.
            let gap = NSBezierPath()
            gap.move(to: from)
            gap.line(to: to)
            gap.lineWidth = 4
            NSGraphicsContext.current?.compositingOperation = .clear
            gap.stroke()
            NSGraphicsContext.current?.compositingOperation = .sourceOver
            let line = NSBezierPath()
            line.move(to: from)
            line.line(to: to)
            line.lineWidth = 1.6
            line.lineCapStyle = .round
            NSColor.black.setStroke()
            line.stroke()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "Twentea is off"
        return image
    }()

    private var text: String? {
        switch scheduler.phase {
        case .onBreak(let s): return BreakView.format(max(0, s.endsAt.timeIntervalSince(scheduler.now)))
        case .due: return "now"
        case .working:
            guard let next = scheduler.nextBreak else { return nil }
            return next.seconds >= 60 ? "\(Int(next.seconds / 60))m" : "\(Int(next.seconds))s"
        default: return nil
        }
    }
}

/// The big round on/off switch. Used in the menu bar panel and the settings window.
struct PowerButton: View {
    @ObservedObject var store: SettingsStore
    var size: CGFloat = 96

    var body: some View {
        Button {
            withAnimation(.spring(duration: 0.3)) { store.isOn.toggle() }
        } label: {
            ZStack {
                Circle()
                    .fill(store.isOn
                          ? AnyShapeStyle(LinearGradient(colors: [Color(red: 1, green: 0.70, blue: 0.28),
                                                                  Color(red: 0.82, green: 0.34, blue: 0.04)],
                                                         startPoint: .top, endPoint: .bottom))
                          : AnyShapeStyle(Color.secondary.opacity(0.18)))
                    .shadow(color: store.isOn ? Color.orange.opacity(0.45) : .clear, radius: size * 0.12)
                Circle().strokeBorder(Color.white.opacity(store.isOn ? 0.5 : 0.15), lineWidth: 1.5)
                Image(systemName: "power")
                    .font(.system(size: size * 0.4, weight: .bold))
                    .foregroundStyle(store.isOn ? Color.white : Color.secondary)
            }
            .frame(width: size, height: size)
            .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help(store.isOn ? "Turn Twentea off" : "Turn Twentea on")
        .accessibilityLabel(store.isOn ? "Twentea is on. Turn off" : "Twentea is off. Turn on")
    }
}

struct MenuView: View {
    let app: AppModel
    @ObservedObject var store: SettingsStore
    @ObservedObject var scheduler: Scheduler
    @ObservedObject var stats: StatsLog
    @ObservedObject var updater: Updater

    var body: some View {
        VStack(spacing: 14) {
            if let version = updater.availableVersion {
                Button { updater.install() } label: {
                    Label("Update to \(version)", systemImage: "arrow.down.circle.fill")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .tint(.orange)
            }
            PowerButton(store: store)
                .padding(.top, 6)
            VStack(spacing: 2) {
                Text(store.isOn ? "Twentea is on" : "Twentea is off").font(.headline)
                Text(status).font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center)
                    .lineLimit(2, reservesSpace: true)
            }

            // The menu bar window does not shrink when content shrinks, so the layout stays the same
            // when off and the controls are just disabled.
            Group {
                Divider()
                actions
                Divider()
                presetPicker
                statsLine
            }
            .disabled(!store.isOn)
            .opacity(store.isOn ? 1 : 0.4)

            Divider()
            HStack {
                Button("Settings…") { SettingsWindow.show() }.keyboardShortcut(",")
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }.keyboardShortcut("q")
            }
            .buttonStyle(.borderless)
        }
        .padding(16)
        .frame(width: 280)
    }

    private var status: String {
        switch scheduler.phase {
        case .off: return "No breaks, dimming or reminders."
        case .offHours: return "Outside working hours"
        case .paused(let until):
            return until.map { "Paused until \($0.formatted(date: .omitted, time: .shortened))" } ?? "Paused"
        case .smartPaused(let reason): return reason
        case .due(let kind, _): return "\(kind.title) at the next pause"
        case .onBreak(let s): return "On \(s.kind.title.lowercased())"
        case .working:
            return BreakKind.allCases.compactMap { kind in
                scheduler.remaining(kind).map { "\(kind.title): \(Int(ceil($0 / 60))) min" }
            }.joined(separator: " · ")
        }
    }

    @ViewBuilder
    private var actions: some View {
        HStack(spacing: 8) {
            Button("Break now") { scheduler.startBreak(.micro) }
            if case .paused = scheduler.phase {
                Button("Resume") { scheduler.resume() }
            } else {
                Menu("Pause") {
                    Button("30 minutes") { scheduler.pause(minutes: 30) }
                    Button("1 hour") { scheduler.pause(minutes: 60) }
                    Button("Until tomorrow") { scheduler.pauseUntilTomorrow() }
                }
                .fixedSize()
            }
        }
        .controlSize(.large)
    }

    private var presetPicker: some View {
        Picker("Preset", selection: Binding(get: { store.activePresetID }, set: { store.apply(presetID: $0) })) {
            ForEach(store.allPresets) { Text($0.name).tag($0.id) }
        }
        .overlay(alignment: .trailing) {
            if store.isCustom { Text("changed").font(.caption2).foregroundStyle(.orange).padding(.trailing, 28) }
        }
    }

    private var statsLine: some View {
        let taken = (stats.today[.done] ?? 0) + (stats.today[.credited] ?? 0)
        let skipped = stats.today[.skipped] ?? 0
        let text = stats.enabled
            ? "Today: \(taken) taken · \(skipped) skipped" + (stats.adherence.map { " · \($0)%" } ?? "")
            : "Stats are off"
        return Text(text)
            .font(.caption).foregroundStyle(.secondary)
    }
}
