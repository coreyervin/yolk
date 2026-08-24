// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Yolk",
    // macOS 14 for MainActor.assumeIsolated in the timer handlers; the menu bar
    // app's @Observable requires it anyway.
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "YolkKit", targets: ["YolkKit"]),
        .executable(name: "yolk", targets: ["yolk"]),
    ],
    targets: [
        .target(name: "YolkKit"),
        .executableTarget(name: "yolk", dependencies: ["YolkKit"]),
        .testTarget(name: "YolkKitTests", dependencies: ["YolkKit"]),
        .testTarget(name: "YolkCLITests", dependencies: ["yolk", "YolkKit"]),
    ]
)
