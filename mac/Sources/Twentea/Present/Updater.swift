import AppKit

/// Checks GitHub releases on launch and every six hours. Update downloads the new app, swaps it in
/// after Twentea quits, and relaunches it. Falls back to the releases page when it cannot.
@MainActor
final class Updater: ObservableObject {
    enum State: Equatable {
        case idle, checking, upToDate, downloading
        case available(String)
        case failed(String)
    }

    @Published private(set) var state: State = .idle
    private var release: UpdateCheck.Release?
    private var timer: Timer?

    var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }

    var availableVersion: String? {
        if case .available(let version) = state { return version }
        return nil
    }

    var statusText: String {
        switch state {
        case .idle: return "Not checked yet"
        case .checking: return "Checking…"
        case .upToDate: return "Twentea \(currentVersion) is the latest version"
        case .downloading: return "Downloading the update…"
        case .available(let v): return "Twentea \(v) is available"
        case .failed(let message): return message
        }
    }

    func start() {
        check()
        timer = Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.check() }
        }
    }

    func check() {
        guard state != .checking, state != .downloading else { return }
        state = .checking
        Task {
            do {
                var request = URLRequest(url: UpdateCheck.latestURL)
                request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
                request.setValue("Twentea/\(currentVersion)", forHTTPHeaderField: "User-Agent")
                let (data, response) = try await URLSession.shared.data(for: request)
                // 404 means the repository has no published release yet.
                if (response as? HTTPURLResponse)?.statusCode == 404 { state = .upToDate; return }
                let latest = try JSONDecoder().decode(UpdateCheck.Release.self, from: data)
                release = latest
                state = UpdateCheck.isNewer(latest.tagName, than: currentVersion)
                    ? .available(UpdateCheck.display(latest.tagName)) : .upToDate
            } catch {
                state = .failed("Could not check for updates")
            }
        }
    }

    func install() {
        let target = Bundle.main.bundleURL
        guard let release, let asset = UpdateCheck.asset(in: release), let url = URL(string: asset.browserDownloadURL),
              target.pathExtension == "app",
              FileManager.default.isWritableFile(atPath: target.deletingLastPathComponent().path) else {
            NSWorkspace.shared.open(UpdateCheck.releasesPage)
            return
        }
        state = .downloading
        Task {
            do {
                let (file, _) = try await URLSession.shared.download(from: url)
                let dir = FileManager.default.temporaryDirectory.appendingPathComponent("twentea-update-\(UUID().uuidString)")
                try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                try Self.run("/usr/bin/ditto", ["-x", "-k", file.path, dir.path])
                let app = dir.appendingPathComponent("Twentea.app")
                guard Bundle(url: app)?.bundleIdentifier == Bundle.main.bundleIdentifier else {
                    throw CocoaError(.fileReadCorruptFile)
                }
                // Wait for this process to exit, swap the bundle, then open the new one.
                let pid = ProcessInfo.processInfo.processIdentifier
                let script = """
                while kill -0 \(pid) 2>/dev/null; do sleep 0.2; done
                rm -rf "\(target.path)" && /usr/bin/ditto "\(app.path)" "\(target.path)" && /usr/bin/open "\(target.path)"
                """
                let swap = Process()
                swap.executableURL = URL(fileURLWithPath: "/bin/sh")
                swap.arguments = ["-c", script]
                try swap.run()
                NSApp.terminate(nil)
            } catch {
                state = .failed("Update failed. Download it from the releases page.")
            }
        }
    }

    private static func run(_ tool: String, _ args: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = args
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw CocoaError(.fileWriteUnknown) }
    }
}
