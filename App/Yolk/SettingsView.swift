import SwiftUI
import YolkAppKit
import YolkKit  // YolkConfig's ranges bound the controls

/// One pane, no tabs. Presentation only.
struct SettingsView: View {
    @Bindable var model: AppModel

    var body: some View {
        Form {
            Section {
                // Plain language rather than raw numbers: the design calls for
                // controls that explain what they do, not what they store.
                LabeledContent("Check every") {
                    Stepper(
                        value: $model.interval, in: YolkConfig.intervalRange, step: 5
                    ) {
                        Text("\(Int(model.interval)) seconds")
                            .monospacedDigit()
                    }
                }
                Text("How often Yolk looks at whether you have gone quiet.")
                    .font(.caption)
                    .foregroundStyle(.secondary)

                LabeledContent("Nudge after") {
                    Stepper(
                        value: $model.threshold, in: YolkConfig.thresholdRange, step: 5
                    ) {
                        Text("\(Int(model.threshold)) seconds idle")
                            .monospacedDigit()
                    }
                }
                Text(
                    "How long you can be idle before Yolk stands in for you. "
                        + "Kept under five minutes so Slack never reaches its away timer."
                )
                .font(.caption)
                .foregroundStyle(.secondary)

                if let displaySleep = model.displaySleepWarning {
                    WarningRow(
                        "Your display sleeps after \(Int(displaySleep / 60)) minutes — sooner "
                            + "than Yolk would act with these settings. Once the display sleeps "
                            + "and locks, Yolk pauses and Slack will mark you away. Lower these "
                            + "numbers, or raise display sleep in System Settings → Lock Screen."
                    )
                }
            }

            Section {
                Toggle("Launch at Login", isOn: $model.launchAtLogin)
                    .disabled(!model.canEnableLaunchAtLogin)
                if let problem = model.launchAtLoginProblem {
                    WarningRow(problem)
                } else {
                    Text("Puts Yolk in your menu bar at login. It starts idle — "
                        + "you switch it on when you need it.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }

            Section {
                LabeledContent("Accessibility") {
                    if model.hasAccessibilityPermission {
                        Label("Granted", systemImage: "checkmark.circle.fill")
                            .foregroundStyle(.green)
                    } else {
                        Label("Not granted", systemImage: "exclamationmark.triangle.fill")
                            .foregroundStyle(.orange)
                    }
                }
                if model.hasAccessibilityPermission {
                    Text("Yolk can keep Slack seeing you as active.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    Text("Without this Yolk still keeps the Mac awake, but Slack will "
                        + "mark you away.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Button("Open Accessibility Settings…") {
                        if let url = AppModel.accessibilitySettingsURL {
                            NSWorkspace.shared.open(url)
                        }
                    }
                }
            }

            // Sits inside the Form as its own unstyled section so it lines up
            // with the panes above and stays out of the way.
            Section {
                Text("Yolk \(YolkKit.version) · © 2026 Corey Ervin")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
                    .frame(maxWidth: .infinity)
            }
            .listRowBackground(Color.clear)
        }
        .formStyle(.grouped)
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
        // System Settings and the app's location can both change while this
        // window is closed, so the state is re-read on appearance.
        .onAppear {
            model.refreshSystemState()
            model.refreshLoginItemState()
        }
    }
}

private struct WarningRow: View {
    let message: String

    init(_ message: String) {
        self.message = message
    }

    var body: some View {
        Label {
            Text(message)
                .font(.caption)
                .fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
        }
    }
}
