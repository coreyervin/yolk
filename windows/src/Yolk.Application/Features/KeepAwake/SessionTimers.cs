using Yolk.Application.Abstractions;
using Yolk.Application.Features.ActivityCheck;
using Yolk.Application.Sessions;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Features.KeepAwake;

/// <summary>
/// The running session's two timers: the periodic activity check, and a
/// one-shot for the timeout. Shared by Start and Reconfigure.
/// </summary>
internal sealed class SessionTimers(
    SessionRuntime runtime,
    IScheduler scheduler,
    ICommandHandler<CheckActivity> checkActivity)
{
    public void Schedule()
    {
        var config = runtime.Session.Config;
        var ticker = scheduler.Schedule(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(config.Interval),
            () => checkActivity.Handle(new CheckActivity()));

        // One-shot so the timeout fires on time rather than up to an interval
        // late. Timers may not count through system sleep, so CheckActivity's
        // deadline test remains the sleep-safe backstop.
        if (config.Timeout is { } timeout)
        {
            var deadline = scheduler.Schedule(
                TimeSpan.FromSeconds(timeout), null, () => runtime.End(StopReason.TimeoutReached));
            runtime.ReplaceTimers(ticker, deadline);
        }
        else
        {
            runtime.ReplaceTimers(ticker);
        }
    }
}
