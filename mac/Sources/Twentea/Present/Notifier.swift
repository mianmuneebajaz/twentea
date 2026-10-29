import AppKit
import UserNotifications

@MainActor
protocol Notifying: AnyObject {
    func notifyBreak(_ kind: BreakKind, seconds: Int)
    func playStart()
    func playEnd()
}

@MainActor
final class Notifier: Notifying {
    func requestAuthorization() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
    }

    func notifyBreak(_ kind: BreakKind, seconds: Int) {
        let content = UNMutableNotificationContent()
        content.title = kind == .micro ? "Look away for \(seconds) seconds" : "Time for a \(seconds / 60)-minute break"
        content.body = kind == .micro
            ? "Find something at least 6 metres away, ideally out a window."
            : "Stand up, walk around, and rest your eyes away from the screen."
        content.interruptionLevel = .timeSensitive
        let request = UNNotificationRequest(identifier: "twentea.break", content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }

    func playStart() { NSSound(named: "Glass")?.play() }
    func playEnd() { NSSound(named: "Tink")?.play() }
}
