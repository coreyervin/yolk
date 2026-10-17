using Yolk.Application.Abstractions;
using Yolk.Application.Sessions;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Features.Status;

/// <summary>What the tray icon and menu show, as of <paramref name="AsOf"/>.</summary>
public sealed record GetStatus(DateTime AsOf);

public sealed record StatusDto(
    bool IsActive,
    PauseReason? PauseReason,
    int ActivityCount,
    DateTime? StartedAt,
    // The menu's first line.
    string Title,
    // The menu's second line — "Awake 2h 14m · 47 nudges", or null while idle.
    string? Detail,
    // Display only, for "Stops at 17:30".
    DateTime? EstimatedEnd,
    // The display-off delay, only when it beats Yolk to the punch.
    double? DisplaySleepWarning);

internal sealed class GetStatusHandler(SessionRuntime runtime, IDisplaySettingsProvider display)
    : IQueryHandler<GetStatus, StatusDto>
{
    public StatusDto Handle(GetStatus query)
    {
        var session = runtime.Session;
        var run = session.Run;
        var displaySleep = display.DisplaySleepSeconds();
        return new StatusDto(
            IsActive: run is not null,
            PauseReason: run?.PausedBecause,
            ActivityCount: run?.ActivityCount ?? 0,
            StartedAt: run?.StartedAt,
            Title: Title(run),
            Detail: run is null ? null : $"Awake {Uptime(run.StartedAt, query.AsOf)} · {Nudges(run.ActivityCount)}",
            EstimatedEnd: run?.EstimatedEnd,
            DisplaySleepWarning: KeepAwakeSession.DisplaySleepBeatsYolk(session.Config, displaySleep)
                ? displaySleep
                : null);
    }

    private static string Title(RunState? run) => run switch
    {
        null => "Yolk is idle",
        { PausedBecause: null } => "Yolk is active",
        { PausedBecause: PauseReason.ScreenLocked } => "Paused — screen locked",
        { PausedBecause: PauseReason.SessionSwitchedOut } => "Paused — another user is using this PC",
        _ => "Paused — session state unavailable",
    };

    /// <summary>"2h 14m". Still shown while paused: the session is running, just not nudging.</summary>
    internal static string Uptime(DateTime startedAt, DateTime asOf)
    {
        var minutes = (int)(Math.Max(0, (asOf - startedAt).TotalSeconds) / 60);
        return minutes < 60 ? $"{minutes}m" : $"{minutes / 60}h {minutes % 60}m";
    }

    private static string Nudges(int count) => count switch
    {
        0 => "no nudges",
        1 => "1 nudge",
        _ => $"{count} nudges",
    };
}
