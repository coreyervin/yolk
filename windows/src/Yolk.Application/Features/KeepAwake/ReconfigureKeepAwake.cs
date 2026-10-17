using Yolk.Application.Abstractions;
using Yolk.Application.Sessions;
using Yolk.Domain.Configuration;

namespace Yolk.Application.Features.KeepAwake;

/// <summary>
/// Apply new settings. A running session keeps its start time and nudge
/// count; a changed timeout restarts the countdown from now. When stopped,
/// the settings are simply kept for the next start.
/// </summary>
public sealed record ReconfigureKeepAwake(YolkConfig Config);

internal sealed class ReconfigureKeepAwakeHandler(SessionRuntime runtime, SessionTimers timers, IClock clock)
    : ICommandHandler<ReconfigureKeepAwake>
{
    public void Handle(ReconfigureKeepAwake command)
    {
        var change = runtime.Session.Reconfigure(command.Config, clock.Now(), clock.MonotonicSeconds());
        if (runtime.Session.IsRunning && change.TimersChanged)
        {
            timers.Schedule();
        }
    }
}
