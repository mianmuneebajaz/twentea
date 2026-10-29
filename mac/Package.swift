// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Twentea",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "Twentea",
            path: "Sources/Twentea",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .testTarget(
            name: "TwenteaTests",
            dependencies: ["Twentea"],
            path: "Tests/TwenteaTests",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
    ]
)
