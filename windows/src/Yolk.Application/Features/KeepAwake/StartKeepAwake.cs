using Yolk.Application.Abstractions;
using Yolk.Application.Sessions;
using Yolk.Domain.Configuration;

namespace Yolk.Application.Features.KeepAwake;

/// <summary>
/// Start keeping the machine awake. <paramref name="Config"/> replaces the
/// current settings first — the CLI passes its flags; the tray app passes
/// nothing and runs with its stored settings.
/// </summary>
public sealed record StartKeepAwake(YolkConfig? Config = null);

public abstract record StartKeepAwakeResult
{
    private StartKeepAwakeResult() { }

    public sealed record Started : StartKeepAwakeResult;

    public sealed record AlreadyRunning : StartKeepAwakeResult;

    /// <summary>The system wouldn't grant the power request. Nothing started.</summary>
    public sealed record PowerRequestRefused(int Code) : StartKeepAwakeResult;
}

internal sealed class StartKeepAwakeHandler(
    SessionRuntime runtime,
    SessionTimers timers,
    IPowerRequestService power,
    IClock clock,
    IDisplaySettingsProvider display)
    : ICommandHandler<StartKeepAwake, StartKeepAwakeResult>
{
    public StartKeepAwakeResult Handle(StartKeepAwake command)
    {
        var session = runtime.Session;
        if (session.IsRunning)
        {
            return new StartKeepAwakeResult.AlreadyRunning();
        }
        if (command.Config is { } config)
        {
            session.Reconfigure(config, clock.Now(), clock.MonotonicSeconds());
        }

        // Acquired before any state changes, so a refusal leaves the session
        // cleanly stopped rather than half-started.
        try
        {
            runtime.PowerRequest = power.Acquire(SessionRuntime.PowerRequestReason);
        }
        catch (PowerRequestRefusedException refused)
        {
            return new StartKeepAwakeResult.PowerRequestRefused(refused.Code);
        }

        runtime.Publish(session.Start(clock.Now(), clock.MonotonicSeconds()));
        runtime.Publish(session.CheckDisplaySleep(display.DisplaySleepSeconds()));
        timers.Schedule();
        return new StartKeepAwakeResult.Started();
    }
}
