// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Yolk",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "YolkKit", targets: ["YolkKit"]),
        .executable(name: "yolk", targets: ["yolk"]),
    ],
    targets: [
        .target(name: "YolkKit"),
        // Still the original self-contained main.swift with top-level mutable
        // globals; Swift 6 mode rejects those. Step 3 ports it onto YolkKit and
        // this override goes away.
        .executableTarget(
            name: "yolk",
            dependencies: ["YolkKit"],
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .testTarget(name: "YolkKitTests", dependencies: ["YolkKit"]),
    ]
)
