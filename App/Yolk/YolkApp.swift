import SwiftUI
import YolkAppKit

/// The app is a thin shell: scenes, and nothing else. Every decision lives in
/// `AppModel` (in the YolkAppKit package target), where it is unit-tested.
@main
struct YolkApp: App {
    @State private var model = AppModel()
    @State private var now = Date()

    /// Minute-resolution uptime only needs a slow tick, and this also picks up
    /// an Accessibility grant made in System Settings while Yolk is running.
    private let heartbeat = Timer.publish(every: 30, on: .main, in: .common).autoconnect()

    var body: some Scene {
        MenuBarExtra {
            MenuBarView(model: model, now: now)
        } label: {
            // Placeholder symbols: the template egg icons arrive in step 6.
            // The yolk-filled/hollow distinction the design calls for is
            // already the shape of this — filled active, hollow idle, dimmed
            // when paused.
            Image(systemName: menuBarSymbol)
                .opacity(model.pauseReason == nil ? 1 : 0.5)
                .onReceive(heartbeat) { tick in
                    now = tick
                    model.refreshSystemState()
                }
        }

        Settings {
            SettingsView(model: model)
        }
    }

    private var menuBarSymbol: String {
        model.isActive ? "largecircle.fill.circle" : "circle"
    }
}
