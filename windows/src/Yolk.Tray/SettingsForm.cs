using System.ComponentModel;
using Yolk.AppCore;
using Yolk.Core;

namespace Yolk.Tray;

/// <summary>One window, no tabs. Presentation only.</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppModel _model;
    private readonly NumericUpDown _interval;
    private readonly NumericUpDown _threshold;
    private readonly Label _displaySleepWarning;
    private readonly CheckBox _launchAtLogin;
    private readonly Label _launchAtLoginNote;

    public SettingsForm(AppModel model)
    {
        _model = model;

        Text = "Yolk Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont ?? Font;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        Controls.Add(layout);

        // Plain language rather than raw numbers: controls that explain what
        // they do, not what they store.
        _interval = AddStepper(layout, "Check every", "seconds",
            YolkConfig.MinInterval, YolkConfig.MaxInterval, model.Interval);
        AddCaption(layout, "How often Yolk looks at whether you have gone quiet.");

        _threshold = AddStepper(layout, "Nudge after", "seconds idle",
            YolkConfig.MinThreshold, YolkConfig.MaxThreshold, model.Threshold);
        AddCaption(layout, "How long you can be idle before Yolk stands in for you. "
            + "Kept under five minutes so Slack never reaches its away timer.");

        _displaySleepWarning = AddCaption(layout, "", warning: true);

        _launchAtLogin = new CheckBox { Text = "Launch at sign-in", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(_launchAtLogin);
        layout.SetColumnSpan(_launchAtLogin, 2);
        _launchAtLoginNote = AddCaption(layout, "");

        var footer = AddCaption(layout, $"Yolk {YolkVersion.Current} · © 2026 Corey Ervin");
        footer.Margin = new Padding(0, 16, 0, 0);

        _interval.ValueChanged += (_, _) => _model.Interval = (double)_interval.Value;
        _threshold.ValueChanged += (_, _) => _model.Threshold = (double)_threshold.Value;
        _launchAtLogin.CheckedChanged += (_, _) => _model.LaunchAtLogin = _launchAtLogin.Checked;
        _model.PropertyChanged += OnModelChanged;

        // The power plan and the startup entry can both change while this
        // window is closed, so the state is re-read whenever it opens.
        _model.RefreshSystemState();
        _model.RefreshLoginItemState();
        Render();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        _interval.Value = (decimal)_model.Interval;
        _threshold.Value = (decimal)_model.Threshold;

        _displaySleepWarning.Visible = _model.DisplaySleepWarning is not null;
        if (_model.DisplaySleepWarning is { } sleep)
        {
            _displaySleepWarning.Text =
                $"⚠ Your display turns off after {(int)(sleep / 60)} minutes — sooner than Yolk "
                + "would act with these settings. Once the display turns off and locks, Yolk "
                + "pauses and Slack will mark you away. Lower these numbers, or raise "
                + "\"Turn off my screen after\" in Settings → System → Power.";
        }

        _launchAtLogin.Enabled = _model.CanEnableLaunchAtLogin;
        _launchAtLogin.Checked = _model.LaunchAtLogin;
        _launchAtLoginNote.Text = _model.LaunchAtLoginProblem is { } problem
            ? $"⚠ {problem}"
            : "Puts Yolk in your system tray when you sign in. It starts idle — "
                + "you switch it on when you need it.";
    }

    private static NumericUpDown AddStepper(
        TableLayoutPanel layout, string label, string unit, double min, double max, double value)
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
            Value = (decimal)value,
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _model.PropertyChanged -= OnModelChanged;
        base.OnFormClosed(e);
    }
}
