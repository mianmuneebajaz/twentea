import AppKit
import IOKit.pwr_mgt
import SwiftUI

/// A borderless panel. Only full overlays take keyboard focus; cards and dims leave it with the app
/// you were using, so typing into a terminal or agent session keeps working.
private final class BreakPanel: NSPanel {
    var takesFocus = false
    override var canBecomeKey: Bool { takesFocus }
}

@MainActor
final class BreakViewModel: ObservableObject {
    @Published var session: BreakSession?
}

/// Draws breaks: floating card, soft dim, full overlay or lock, on every screen.
@MainActor
final class BreakPresenter: BreakPresenting {
    weak var scheduler: Scheduler?
    private let model = BreakViewModel()
    private var panels: [NSPanel] = []
    /// The app that had focus before an overlay took it, so it gets focus back afterwards.
    private var previousApp: NSRunningApplication?
    private var sleepAssertion: IOPMAssertionID?

    func show(_ session: BreakSession) {
        hide()
        model.session = session
        guard let scheduler else { return }
        keepSystemAwake()

        switch session.level {
        case .menuBar, .notification:
            break
        case .card:
            addCard(scheduler, centered: false)
        case .softDim:
            addDim(alpha: 0.45, blocking: false)
            addCard(scheduler, centered: true)
        case .overlay, .lock:
            if let front = NSWorkspace.shared.frontmostApplication, front != NSRunningApplication.current {
                previousApp = front
            }
            addDim(alpha: 0.9, blocking: true, content: scheduler)
            NSApp.activate(ignoringOtherApps: true)
            if let focus = panels.first(where: { ($0 as? BreakPanel)?.takesFocus == true }) {
                focus.makeKeyAndOrderFront(nil)
            }
        }
    }

    func update(_ session: BreakSession) {
        model.session = session
    }

    func hide() {
        for panel in panels {
            NSAnimationContext.runAnimationGroup({ $0.duration = 0.25; panel.animator().alphaValue = 0 },
                                                 completionHandler: { panel.close() })
        }
        panels.removeAll()
        model.session = nil
        allowSystemSleep()
        if let app = previousApp {
            previousApp = nil
            app.activate()
        }
    }

    // MARK: - Power

    /// Stops idle system sleep during a break, so long breaks never suspend running work such as
    /// agent sessions, builds or downloads. The display may still sleep.
    private func keepSystemAwake() {
        guard sleepAssertion == nil else { return }
        var id = IOPMAssertionID(0)
        let result = IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleSystemSleep as CFString,
                                                 IOPMAssertionLevel(kIOPMAssertionLevelOn),
                                                 "Twentea break in progress" as CFString, &id)
        if result == kIOReturnSuccess { sleepAssertion = id }
    }

    private func allowSystemSleep() {
        if let id = sleepAssertion { IOPMAssertionRelease(id) }
        sleepAssertion = nil
    }

    // MARK: - Windows

    /// Hosting view that keeps the panel's frame instead of resizing it to the SwiftUI content.
    private static func host<V: View>(_ view: V) -> NSView {
        let hosting = NSHostingView(rootView: view)
        hosting.sizingOptions = []
        return hosting
    }

    private func makePanel(frame: NSRect, level: NSWindow.Level, blocking: Bool) -> NSPanel {
        let panel = BreakPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel],
                               backing: .buffered, defer: false)
        panel.level = level
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.isReleasedWhenClosed = false
        panel.ignoresMouseEvents = !blocking
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        return panel
    }

    private func fadeIn(_ panel: NSPanel) {
        panel.alphaValue = 0
        panel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.6
            panel.animator().alphaValue = 1
        }
        panels.append(panel)
    }

    private func addDim(alpha: CGFloat, blocking: Bool, content scheduler: Scheduler? = nil) {
        let main = NSScreen.main
        for screen in NSScreen.screens {
            let panel = makePanel(frame: screen.frame, level: .screenSaver, blocking: blocking)
            if let scheduler, screen == main {
                (panel as? BreakPanel)?.takesFocus = blocking
                let view = ZStack {
                    Color.black.opacity(alpha)
                    BreakView(model: model, scheduler: scheduler, style: .overlay)
                }
                panel.contentView = Self.host(view)
            } else {
                panel.backgroundColor = NSColor.black.withAlphaComponent(alpha)
            }
            fadeIn(panel)
        }
    }

    private func addCard(_ scheduler: Scheduler, centered: Bool) {
        guard let screen = NSScreen.main else { return }
        let size = NSSize(width: 340, height: 170)
        let visible = screen.visibleFrame
        let origin = centered
            ? NSPoint(x: visible.midX - size.width / 2, y: visible.midY - size.height / 2)
            : NSPoint(x: visible.maxX - size.width - 16, y: visible.maxY - size.height - 16)
        let panel = makePanel(frame: NSRect(origin: origin, size: size),
                              level: centered ? NSWindow.Level(NSWindow.Level.screenSaver.rawValue + 1) : .floating,
                              blocking: true)
        panel.hasShadow = true
        panel.contentView = Self.host(BreakView(model: model, scheduler: scheduler, style: .card))
        fadeIn(panel)
    }
}
