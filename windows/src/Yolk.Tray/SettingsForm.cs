using Yolk.Application.Features.LaunchAtSignIn;
using Yolk.Application.Features.Settings;
using Yolk.Domain;
using Yolk.Domain.Configuration;

namespace Yolk.Tray;

/// <summary>One window, no tabs. Presentation only.</summary>
internal sealed class SettingsForm : Form
{
    private readonly TrayHandlers _handlers;
    private readonly NumericUpDown _interval;
    private readonly NumericUpDown _threshold;
    private readonly Label _displaySleepWarning;
    private readonly CheckBox _launchAtSignIn;
    private readonly Label _launchAtSignInNote;
    private LaunchAtSignInStatus _launchStatus;
    private bool _rendering;

    public SettingsForm(TrayHandlers handlers)
    {
        _handlers = handlers;

        Text = "Yolk Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont ?? Font;

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        Controls.Add(layout);

        // Plain language rather than raw numbers: controls that explain what
        // they do, not what they store.
        _interval = AddStepper(layout, "Check every", "seconds", YolkConfig.MinInterval, YolkConfig.MaxInterval);
        AddCaption(layout, "How often Yolk looks at whether you have gone quiet.");

        _threshold = AddStepper(layout, "Nudge after", "seconds idle", YolkConfig.MinThreshold, YolkConfig.MaxThreshold);
        AddCaption(layout, "How long you can be idle before Yolk stands in for you. "
            + "Kept under five minutes so Slack never reaches its away timer.");

        _displaySleepWarning = AddCaption(layout, "", warning: true);

        _launchAtSignIn = new CheckBox { Text = "Launch at sign-in", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(_launchAtSignIn);
        layout.SetColumnSpan(_launchAtSignIn, 2);
        _launchAtSignInNote = AddCaption(layout, "");

        var footer = AddCaption(layout, $"Yolk {YolkVersion.Current} · © 2026 Corey Ervin");
        footer.Margin = new Padding(0, 16, 0, 0);

        _interval.ValueChanged += (_, _) => Change(new UpdateSettings(Interval: (double)_interval.Value));
        _threshold.ValueChanged += (_, _) => Change(new UpdateSettings(Threshold: (double)_threshold.Value));
        _launchAtSignIn.CheckedChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _launchStatus = _handlers.SetLaunchAtSignIn.Handle(new SetLaunchAtSignIn(_launchAtSignIn.Checked));
                Render();
            }
        };

        // The power plan and the startup entry can both change while this
        // window is closed, so the state is re-read whenever it opens.
        _launchStatus = _handlers.GetLaunchAtSignIn.Handle(new GetLaunchAtSignIn());
        Render();
    }

    private void Change(UpdateSettings update)
    {
        if (!_rendering)
        {
            _handlers.UpdateSettings.Handle(update);
            Render();
        }
    }

    private void Render()
    {
        _rendering = true;
        try
        {
            var settings = _handlers.GetSettings.Handle(new GetSettings());
            _interval.Value = (decimal)settings.Interval;
            _threshold.Value = (decimal)settings.Threshold;

            var warning = _handlers.Status().DisplaySleepWarning;
            _displaySleepWarning.Visible = warning is not null;
            if (warning is { } sleep)
            {
                _displaySleepWarning.Text =
                    $"⚠ Your display turns off after {(int)(sleep / 60)} minutes — sooner than Yolk "
                    + "would act with these settings. Once the display turns off and locks, Yolk "
                    + "pauses and Slack will mark you away. Lower these numbers, or raise "
                    + "\"Turn off my screen after\" in Settings → System → Power.";
            }

            _launchAtSignIn.Enabled = _launchStatus.CanEnable;
            _launchAtSignIn.Checked = _launchStatus.Enabled;
            _launchAtSignInNote.Text = _launchStatus.Problem is { } problem
                ? $"⚠ {problem}"
                : "Puts Yolk in your system tray when you sign in. It starts idle — "
                    + "you switch it on when you need it.";
        }
        finally
        {
            _rendering = false;
        }
    }

    private static NumericUpDown AddStepper(TableLayoutPanel layout, string label, string unit, double min, double max)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 12, 12, 0),
        });

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        var stepper = new NumericUpDown
        {
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = 5,
            Width = 64,
        };
        row.Controls.Add(stepper);
        row.Controls.Add(new Label { Text = unit, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 0, 0) });
        layout.Controls.Add(row);
        return stepper;
    }

    private static Label AddCaption(TableLayoutPanel layout, string text, bool warning = false)
    {
        var caption = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            ForeColor = warning ? Color.DarkOrange : SystemColors.GrayText,
            Margin = new Padding(0, 4, 0, 0),
        };
        layout.Controls.Add(caption);
        layout.SetColumnSpan(caption, 2);
        return caption;
    }
}
