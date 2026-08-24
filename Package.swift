// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Yolk",
    // macOS 14 for MainActor.assumeIsolated in the timer handlers; the menu bar
    // app's @Observable requires it anyway.
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "YolkKit", targets: ["YolkKit"]),
        // Linked by the Xcode app target, which keeps no logic of its own.
        .library(name: "YolkAppKit", targets: ["YolkAppKit"]),
        .executable(name: "yolk", targets: ["yolk"]),
    ],
    targets: [
        .target(name: "YolkKit"),
        .target(name: "YolkAppKit", dependencies: ["YolkKit"]),
        .executableTarget(name: "yolk", dependencies: ["YolkKit"]),
        // Test-only fakes, shared by the suites. `swift build` does compile it
        // (SwiftPM builds every target, not just the ones behind products), but
        // it is not a product and only test targets depend on it, so it is
        // never linked into the CLI or the app.
        .target(name: "YolkTestSupport", dependencies: ["YolkKit"]),
        .testTarget(name: "YolkKitTests", dependencies: ["YolkKit", "YolkTestSupport"]),
        .testTarget(name: "YolkCLITests", dependencies: ["yolk", "YolkKit"]),
        .testTarget(
            name: "YolkAppKitTests", dependencies: ["YolkAppKit", "YolkTestSupport"]),
    ]
)
