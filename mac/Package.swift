// swift-tools-version:5.9
// Chipmunk for Mac: a native shell (menu bar item, panel window, capture, paste) around the
// same panel UI the Windows app uses (../ui). Build with scripts/build-app.sh.
import PackageDescription

let package = Package(
    name: "Chipmunk",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(name: "Chipmunk", path: "Sources/Chipmunk"),
        .testTarget(name: "ChipmunkTests", dependencies: ["Chipmunk"], path: "Tests/ChipmunkTests"),
    ]
)
