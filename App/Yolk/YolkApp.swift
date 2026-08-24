import SwiftUI
import YolkAppKit

/// The app is a thin shell: scenes, and nothing else. Every decision lives in
/// `AppModel` (in the YolkAppKit package target), where it is unit-tested.
@main
struct YolkApp: App {
    @State private var model = AppModel()

    var body: some Scene {
        // Placeholder menu — MenuBarView lands in step 5, the template icons
        // in step 6. Enough to launch the app and exercise AppModel by hand.
        MenuBarExtra("Yolk", systemImage: model.isActive ? "largecircle.fill.circle" : "circle") {
            Button(model.isActive ? "Stop Keeping Mac Awake" : "Keep Mac Awake") {
                model.toggle()
            }
            Divider()
            SettingsLink { Text("Settings…") }
            Button("Quit Yolk") { NSApplication.shared.terminate(nil) }
        }

        Settings {
            Text("Settings arrive in step 5.")
                .frame(width: 420, height: 180)
        }
    }
}
