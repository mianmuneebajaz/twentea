import AppKit
import CoreAudio
import CoreMediaIO
import IOKit.pwr_mgt

/// What the scheduler needs to know about the user. A protocol so tests can drive it.
@MainActor
protocol ActivitySource: AnyObject {
    var idleSeconds: TimeInterval { get }
    var lastAppSwitch: Date { get }
    func smartPauseReason(_ rules: SmartPauseRules) -> String?
}

/// Reads idle time, app switches and call/video signals. Needs no permission prompts.
@MainActor
final class ActivityMonitor: ActivitySource {
    private(set) var lastAppSwitch = Date.distantPast
    private var observer: NSObjectProtocol?

    init() {
        observer = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.lastAppSwitch = Date() }
        }
    }

    var idleSeconds: TimeInterval {
        CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)
    }

    func smartPauseReason(_ rules: SmartPauseRules) -> String? {
        if let bundleID = NSWorkspace.shared.frontmostApplication?.bundleIdentifier,
           rules.excludedBundleIDs.contains(bundleID) {
            return "Paused for \(NSWorkspace.shared.frontmostApplication?.localizedName ?? bundleID)"
        }
        if rules.cameraOrMic, Self.isCameraInUse() || Self.isMicrophoneInUse() {
            return "Paused during a call"
        }
        if rules.videoPlaying, Self.isDisplaySleepPrevented() {
            return "Paused while video plays"
        }
        return nil
    }

    // MARK: - Signals

    static func isCameraInUse() -> Bool {
        var addr = CMIOObjectPropertyAddress(
            mSelector: CMIOObjectPropertySelector(kCMIOHardwarePropertyDevices),
            mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeGlobal),
            mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementMain))
        var size: UInt32 = 0
        let system = CMIOObjectID(kCMIOObjectSystemObject)
        guard CMIOObjectGetPropertyDataSize(system, &addr, 0, nil, &size) == noErr, size > 0 else { return false }
        var devices = [CMIOObjectID](repeating: 0, count: Int(size) / MemoryLayout<CMIOObjectID>.size)
        var used: UInt32 = 0
        guard CMIOObjectGetPropertyData(system, &addr, 0, nil, size, &used, &devices) == noErr else { return false }

        for device in devices {
            var running: UInt32 = 0
            var runAddr = CMIOObjectPropertyAddress(
                mSelector: CMIOObjectPropertySelector(kCMIODevicePropertyDeviceIsRunningSomewhere),
                mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeWildcard),
                mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementWildcard))
            var got: UInt32 = 0
            if CMIOObjectGetPropertyData(device, &runAddr, 0, nil, UInt32(MemoryLayout<UInt32>.size), &got, &running) == noErr,
               running != 0 {
                return true
            }
        }
        return false
    }

    static func isMicrophoneInUse() -> Bool {
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard AudioObjectGetPropertyDataSize(system, &addr, 0, nil, &size) == noErr, size > 0 else { return false }
        var devices = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &addr, 0, nil, &size, &devices) == noErr else { return false }

        for device in devices {
            var streamsAddr = AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyStreams,
                mScope: kAudioDevicePropertyScopeInput,
                mElement: kAudioObjectPropertyElementMain)
            var streamSize: UInt32 = 0
            guard AudioObjectGetPropertyDataSize(device, &streamsAddr, 0, nil, &streamSize) == noErr, streamSize > 0 else { continue }

            var running: UInt32 = 0
            var runSize = UInt32(MemoryLayout<UInt32>.size)
            var runAddr = AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyDeviceIsRunningSomewhere,
                mScope: kAudioObjectPropertyScopeGlobal,
                mElement: kAudioObjectPropertyElementMain)
            if AudioObjectGetPropertyData(device, &runAddr, 0, nil, &runSize, &running) == noErr, running != 0 {
                return true
            }
        }
        return false
    }

    /// Video players and call apps hold a display-sleep assertion while active.
    static func isDisplaySleepPrevented() -> Bool {
        var status: Unmanaged<CFDictionary>?
        guard IOPMCopyAssertionsStatus(&status) == kIOReturnSuccess,
              let dict = status?.takeRetainedValue() as? [String: Int] else { return false }
        return (dict[kIOPMAssertionTypePreventUserIdleDisplaySleep as String] ?? 0) > 0
    }
}
