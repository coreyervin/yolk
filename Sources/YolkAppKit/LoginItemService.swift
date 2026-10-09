import Foundation
import ServiceManagement

/// The Launch at Login boundary.
///
/// `SMAppService` cannot be exercised in a test — it registers a real login
/// item for the real bundle — so it sits behind closures like every other
/// system boundary in this package.
public struct LoginItemService: Sendable {
    public var isEnabled: @Sendable () -> Bool
    public var register: @Sendable () throws -> Void
    public var unregister: @Sendable () throws -> Void
    /// Whether the app is somewhere `SMAppService` can register from.
    public var isInApplicationsFolder: @Sendable () -> Bool

    public init(
        isEnabled: @escaping @Sendable () -> Bool,
        register: @escaping @Sendable () throws -> Void,
        unregister: @escaping @Sendable () throws -> Void,
        isInApplicationsFolder: @escaping @Sendable () -> Bool
    ) {
        self.isEnabled = isEnabled
        self.register = register
        self.unregister = unregister
        self.isInApplicationsFolder = isInApplicationsFolder
    }

    public static let live = LoginItemService(
        isEnabled: { SMAppService.mainApp.status == .enabled },
        register: { try SMAppService.mainApp.register() },
        unregister: { try SMAppService.mainApp.unregister() },
        isInApplicationsFolder: {
            // Registration only works reliably from an Applications directory.
            // The Homebrew cask installs there; a copy left in ~/Downloads does
            // not, and the toggle would fail with nothing explaining why.
            let path = Bundle.main.bundleURL.resolvingSymlinksInPath().path
            return path.hasPrefix("/Applications/")
                || path.hasPrefix(
                    FileManager.default.homeDirectoryForCurrentUser
                        .appendingPathComponent("Applications").path + "/")
        })
}
