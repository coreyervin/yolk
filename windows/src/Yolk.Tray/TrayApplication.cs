using System.ComponentModel;
using Yolk.AppCore;
using Yolk.Core;
using Yolk.Platform.Windows;

namespace Yolk.Tray;

/// <summary>
/// The tray icon and its menu — the counterpart of the Mac's menu bar extra.
/// Presentation only: every decision it shows comes from <see cref="AppModel"/>.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly AppModel _model;
    private readonly NotifyIcon _icon;
    private readonly TrayIcons _icons = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 30_000 };
    private SettingsForm? _settings;

    public TrayApplication()
    {
        _model = new AppModel(
            new Win32Environment(),
            new WinFormsScheduler(),
            new RegistrySettingsStore(),
            new RunKeyLoginItem());
        _model.PropertyChanged += OnModelChanged;

        // Rebuilt each time it opens, so the uptime line is current.
        _menu.Opening += (_, _) => BuildMenu();

        _icon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true,
        };
        // Left click toggles; right click opens the menu.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _model.Toggle();
            }
        };

        // Minute-resolution uptime only needs a slow tick, and this also picks
        // up a power-plan change made while Yolk is running.
        _heartbeat.Tick += (_, _) =>
        {
            _model.RefreshSystemState();
            UpdateIcon();
        };
        _heartbeat.Start();

        UpdateIcon();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppModel.IsActive) or nameof(AppModel.PauseReason)
            or nameof(AppModel.ActivityCount))
        {
            UpdateIcon();
        }
    }

    /// <summary>
    /// State reads off the icon alone: full colour when active, grey when
    /// idle, faded when paused.
    /// </summary>
    private void UpdateIcon()
    {
        _icon.Icon = !_model.IsActive ? _icons.Idle
            : _model.PauseReason is null ? _icons.Active
            : _icons.Paused;
        _icon.Text = TooltipText();
    }

    // NotifyIcon.Text is capped at 127 characters.
    private string TooltipText()
    {
        var detail = _model.StatusDetail(DateTime.Now);
        var text = detail is null ? _model.StatusTitle : $"{_model.StatusTitle}\n{detail}";
        return text.Length <= 127 ? text : text[..127];
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();

        _menu.Items.Add(new ToolStripMenuItem(_model.StatusTitle) { Enabled = false });
        if (_model.StatusDetail(DateTime.Now) is { } detail)
        {
            _menu.Items.Add(new ToolStripMenuItem(detail) { Enabled = false });
        }
        if (_model.EstimatedEnd is { } end)
        {
            _menu.Items.Add(new ToolStripMenuItem($"Stops at {end:t}") { Enabled = false });
        }

        _menu.Items.Add(new ToolStripSeparator());

        var keepAwake = new ToolStripMenuItem("Keep PC Awake") { Checked = _model.IsActive };
        keepAwake.Click += (_, _) => _model.Toggle();
        _menu.Items.Add(keepAwake);

        _menu.Items.Add(new ToolStripSeparator());

        var stopAfter = new ToolStripMenuItem("Stop after…");
        foreach (var option in Enum.GetValues<StopAfter>())
        {
            var item = new ToolStripMenuItem(option.Title()) { Checked = _model.StopAfter == option };
            item.Click += (_, _) => _model.StopAfter = option;
            stopAfter.DropDownItems.Add(item);
        }
        _menu.Items.Add(stopAfter);

        _menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => ShowSettings();
        _menu.Items.Add(settings);

        var quit = new ToolStripMenuItem("Quit Yolk");
        quit.Click += (_, _) => ExitThread();
        _menu.Items.Add(quit);
    }

    private void ShowSettings()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm(_model);
        _settings.Show();
    }

    protected override void ExitThreadCore()
    {
        // Release the power request before the process goes, so the machine's
        // sleep settings are back to normal the moment Yolk quits.
        _model.SetActive(false);
        _icon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _heartbeat.Dispose();
            _settings?.Dispose();
            _icon.Dispose();
            _menu.Dispose();
            _icons.Dispose();
            _model.Dispose();
        }
        base.Dispose(disposing);
    }
}
