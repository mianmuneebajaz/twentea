import Foundation
import Testing
@testable import Twentea

struct UpdateCheckTests {
    @Test func newerReleaseIsDetected() {
        #expect(UpdateCheck.isNewer("v0.2.0", than: "0.1.0"))
        #expect(UpdateCheck.isNewer("v0.10.0", than: "0.9.3"))
        #expect(UpdateCheck.isNewer("v1.0.0-beta.1", than: "0.9"))
        #expect(!UpdateCheck.isNewer("v0.1.0", than: "0.1.0"))
        #expect(!UpdateCheck.isNewer("v0.1.0", than: "0.2.0"))
        #expect(!UpdateCheck.isNewer("nightly", than: "0.1.0"))
    }

    @Test func updateAssetMatchesPlatform() {
        let names = ["Twentea-0.2.0.dmg", "Twentea-0.2.0-mac.zip", "Twentea-0.2.0-win-x64-setup.exe",
                     "Twentea-0.2.0-win-arm64-setup.exe", "Twentea-0.2.0-win-x64.zip"]
        let release = UpdateCheck.Release(tagName: "v0.2.0", assets: names.map { .init(name: $0, browserDownloadURL: "https://example.com/\($0)") })
        #expect(UpdateCheck.asset(in: release)?.name == "Twentea-0.2.0-mac.zip")
    }
}
