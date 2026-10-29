import AppKit
import Combine
import ServiceManagement
import SwiftUI

/// Wires the store, scheduler and presenter together. One per app.
@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    let store = SettingsStore()
    let stats = StatsLog()
    let notifier = Notifier()
    let presenter = BreakPresenter()
    let activity = ActivityMonitor()
    let updater = Updater()
    let scheduler: Scheduler
    private var cancellables: Set<AnyCancellable> = []

    private init() {
        scheduler = Scheduler(settings: store.settings, activity: activity, presenter: presenter,
                              notifier: notifier, stats: stats)
        presenter.scheduler = scheduler
        store.$settings
            .sink { [weak self] s in self?.scheduler.settings = s }
            .store(in: &cancellables)
        store.$isOn
            .sink { [weak self] on in self?.scheduler.isOn = on }
            .store(in: &cancellables)
    }

    func start() {
        notifier.requestAuthorization()
        scheduler.start()
        updater.start()
    }

    var launchAtLogin: Bool {
        get { SMAppService.mainApp.status == .enabled }
        set {
            objectWillChange.send()
            try? newValue ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister()
        }
    }

    /// Handles twentea:// URLs from Raycast, Alfred, Shortcuts or `open`.
    func handle(_ url: URL) {
        guard url.scheme == "twentea" else { return }
        let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
        let value = { (name: String) in items.first { $0.name == name }?.value }
        switch url.host {
        case "on": store.isOn = true
        case "off": store.isOn = false
        case "toggle": store.isOn.toggle()
        case "break": scheduler.startBreak(value("type") == "long" ? .long : .micro)
        case "pause": scheduler.pause(minutes: Int(value("minutes") ?? "") ?? 30)
        case "resume": scheduler.resume()
        case "preset": if let id = value("id") { store.apply(presetID: id) }
        case "settings": SettingsWindow.show()
        default: break
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        MainActor.assumeIsolated {
            let app = AppModel.shared
            app.start()
            if app.store.isFirstLaunch {
                app.store.markLaunched()
                SettingsWindow.show()
            }
        }
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        MainActor.assumeIsolated {
            urls.forEach(AppModel.shared.handle)
        }
    }
}

@main
struct TwenteaApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        MenuBarExtra {
            MenuView(app: AppModel.shared, store: AppModel.shared.store,
                     scheduler: AppModel.shared.scheduler, stats: AppModel.shared.stats,
                     updater: AppModel.shared.updater)
        } label: {
            MenuBarLabel(scheduler: AppModel.shared.scheduler, store: AppModel.shared.store)
        }
        .menuBarExtraStyle(.window)
    }
}

/// The settings window, managed by hand so it opens reliably from a menu bar app.
@MainActor
enum SettingsWindow {
    private static var window: NSWindow?

    static func show() {
        if window == nil {
            let app = AppModel.shared
            let root = SettingsView(app: app, store: app.store, scheduler: app.scheduler)
            let w = NSWindow(contentViewController: NSHostingController(rootView: root))
            w.title = "Twentea Settings"
            w.setContentSize(NSSize(width: 760, height: 540))
            w.styleMask.insert(.resizable)
            w.isReleasedWhenClosed = false
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}
