using Yolk.Application.Features.KeepAwake;
using Yolk.Application.Features.Settings;
using Yolk.Application.Sessions;
using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;

namespace Yolk.Tray;

/// <summary>
/// The tray icon and its menu — the counterpart of the Mac's menu bar extra.
/// Presentation only: every decision it shows comes from a slice.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly TrayHandlers _handlers;
    private readonly ISessionEvents _events;
    private readonly NotifyIcon _icon;
    private readonly TrayIcons _icons = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 30_000 };
    private SettingsForm? _settings;

    public TrayApplication(TrayHandlers handlers, ISessionEvents events)
    {
        _handlers = handlers;
        _events = events;

        // Starts idle, with the settings the user chose last time: opening the
        // app makes Yolk available, it does not begin a session.
        _handlers.ApplyStoredSettings.Handle(new ApplyStoredSettings());
        _events.Emitted += OnSessionEvent;

        // Rebuilt each time it opens, so the uptime line is current.
        _menu.Opening += (_, _) => BuildMenu();

        _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        // Left click toggles; right click opens the menu.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _handlers.Toggle();
            }
        };

        // Keeps the tooltip's uptime current.
        _heartbeat.Tick += (_, _) => UpdateIcon();
        _heartbeat.Start();

        UpdateIcon();
    }

    private void OnSessionEvent(YolkEvent e)
    {
        // Checked fires every tick and changes nothing the icon shows.
        if (e is not YolkEvent.Checked)
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
        var status = _handlers.Status();
        _icon.Icon = !status.IsActive ? _icons.Idle
            : status.PauseReason is null ? _icons.Active
            : _icons.Paused;
        // NotifyIcon.Text is capped at 127 characters.
        var text = status.Detail is null ? status.Title : $"{status.Title}\n{status.Detail}";
        _icon.Text = text.Length <= 127 ? text : text[..127];
    }

    private void BuildMenu()
    {
        var status = _handlers.Status();
        _menu.Items.Clear();

        _menu.Items.Add(new ToolStripMenuItem(status.Title) { Enabled = false });
        if (status.Detail is { } detail)
        {
            _menu.Items.Add(new ToolStripMenuItem(detail) { Enabled = false });
        }
        if (status.EstimatedEnd is { } end)
        {
            _menu.Items.Add(new ToolStripMenuItem($"Stops at {end:t}") { Enabled = false });
        }

        _menu.Items.Add(new ToolStripSeparator());

        var keepAwake = new ToolStripMenuItem("Keep PC Awake") { Checked = status.IsActive };
        keepAwake.Click += (_, _) => _handlers.Toggle();
        _menu.Items.Add(keepAwake);

        _menu.Items.Add(new ToolStripSeparator());

        var current = _handlers.GetSettings.Handle(new GetSettings()).StopAfter;
        var stopAfter = new ToolStripMenuItem("Stop after…");
        foreach (var option in Enum.GetValues<StopAfter>())
        {
            var item = new ToolStripMenuItem(option.Title()) { Checked = current == option };
            item.Click += (_, _) => _handlers.UpdateSettings.Handle(new UpdateSettings(StopAfter: option));
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
        _settings = new SettingsForm(_handlers) { Icon = _icons.App };
        _settings.Show();
    }

    protected override void ExitThreadCore()
    {
        // Release the power request before the process goes, so the machine's
        // sleep settings are back to normal the moment Yolk quits.
        _handlers.Stop.Handle(new StopKeepAwake());
        _icon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _events.Emitted -= OnSessionEvent;
            _heartbeat.Dispose();
            _settings?.Dispose();
            _icon.Dispose();
            _menu.Dispose();
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }
}
