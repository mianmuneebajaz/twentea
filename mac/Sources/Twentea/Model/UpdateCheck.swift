import Foundation

/// Reads the latest GitHub release and decides whether it is newer. Mirrors windows/src/Twentea.Core/UpdateCheck.cs.
enum UpdateCheck {
    static let latestURL = URL(string: "https://api.github.com/repos/mianmuneebajaz/twentea/releases/latest")!
    static let releasesPage = URL(string: "https://github.com/mianmuneebajaz/twentea/releases/latest")!

    struct Asset: Decodable, Equatable {
        var name: String
        var browserDownloadURL: String

        enum CodingKeys: String, CodingKey {
            case name
            case browserDownloadURL = "browser_download_url"
        }
    }

    struct Release: Decodable, Equatable {
        var tagName: String
        var assets: [Asset]

        enum CodingKeys: String, CodingKey {
            case tagName = "tag_name"
            case assets
        }
    }

    /// "v1.2.3", "1.2", "1.2.3-beta.1" become [1, 2, 3]. Anything else is nil.
    static func version(_ tag: String) -> [Int]? {
        var text = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
        if let cut = text.firstIndex(where: { $0 == "-" || $0 == "+" }) { text = String(text[..<cut]) }
        let parts = text.split(separator: ".", omittingEmptySubsequences: false).map { Int($0) }
        guard !parts.isEmpty, parts.count <= 3, parts.allSatisfy({ $0 != nil }) else { return nil }
        return parts.compactMap { $0 } + Array(repeating: 0, count: 3 - parts.count)
    }

    static func isNewer(_ latest: String, than current: String) -> Bool {
        guard let l = version(latest), let c = version(current) else { return false }
        return c.lexicographicallyPrecedes(l)
    }

    static func display(_ tag: String) -> String {
        tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
    }

    /// The Mac update is the zipped app bundle, which can replace the running app in place.
    static func asset(in release: Release) -> Asset? {
        release.assets.first { $0.name.hasSuffix("-mac.zip") }
    }
}
