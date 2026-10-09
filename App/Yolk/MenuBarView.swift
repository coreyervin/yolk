import SwiftUI
import YolkAppKit

/// The menu bar dropdown. Presentation only — every decision it renders comes
/// from `AppModel`, where it is unit-tested.
struct MenuBarView: View {
    @Bindable var model: AppModel
    /// Drives the uptime line. The menu is only built while open, so this
    /// ticks just often enough to keep "Awake 2h 14m" honest.
    let now: Date

    var body: some View {
        Text(model.statusTitle)
        if let detail = model.statusDetail(asOf: now) {
            Text(detail)
        }
        if let end = model.estimatedEnd {
            Text("Stops at \(end.formatted(date: .omitted, time: .shortened))")
        }

        Divider()

        Toggle("Keep Mac Awake", isOn: keepAwake)
            .keyboardShortcut("k")

        Divider()

        // A Picker renders as a submenu with a checkmark on the selection,
        // which is exactly the "Stop after…" section the design calls for.
        Picker("Stop after…", selection: $model.stopAfter) {
            ForEach(AppModel.StopAfter.allCases, id: \.self) { option in
                Text(option.title).tag(option)
            }
        }

        Divider()

        // Only shown when it is actionable. A permanent warning row would
        // become furniture and stop being read.
        if !model.hasAccessibilityPermission {
            Button("⚠ Grant Accessibility Permission…") {
                if let url = AppModel.accessibilitySettingsURL {
                    NSWorkspace.shared.open(url)
                }
            }
        }

        SettingsLink { Text("Settings…") }
            .keyboardShortcut(",")
        Button("Quit Yolk") { NSApplication.shared.terminate(nil) }
            .keyboardShortcut("q")
    }

    /// `isActive` is read-only on purpose — starting a session can fail, so the
    /// model decides what the toggle ends up showing.
    private var keepAwake: Binding<Bool> {
        Binding(get: { model.isActive }, set: { model.setActive($0) })
    }
}
