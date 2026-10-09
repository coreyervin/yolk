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
            // Template images, so macOS inverts and tints them for light and
            // dark menu bars. State reads off the yolk alone: filled when
            // active, hollow when idle, dimmed when paused — all three survive
            // the 18pt menu bar better than a colour or badge change would.
            Image(model.isActive ? "MenuBarActive" : "MenuBarIdle")
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
}
