using Yolk.Application.Abstractions;
using Yolk.Application.Features.ActivityCheck;
using Yolk.Application.Sessions;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Features.KeepAwake;

/// <summary>
/// The running session's two timers: the activity check, and a one-shot for
/// the timeout. Shared by Start and Reconfigure.
/// </summary>
/// <remarks>
/// The check is one-shot too, rescheduled after each run for whenever that
/// check says the next is due — timed to land as idle reaches the random
/// nudge point, so the nudges fall anywhere in the range rather than on a
/// fixed tick.
/// </remarks>
internal sealed class SessionTimers(
    SessionRuntime runtime,
    IScheduler scheduler,
    ICommandHandler<CheckActivity> checkActivity)
{
    public void Schedule()
    {
        // The first check comes a second after starting.
        ScheduleCheck(TimeSpan.FromSeconds(1));

        // Timers may not count through system sleep, so CheckActivity's
        // deadline test remains the sleep-safe backstop.
        runtime.ReplaceDeadline(runtime.Session.Config.Timeout is { } timeout
            ? scheduler.Schedule(TimeSpan.FromSeconds(timeout), null, () => runtime.End(StopReason.TimeoutReached))
            : null);
    }

    private void ScheduleCheck(TimeSpan delay) =>
        runtime.ReplaceTicker(scheduler.Schedule(delay, null, () =>
        {
            checkActivity.Handle(new CheckActivity());
            if (runtime.Session.IsRunning)
            {
                ScheduleCheck(TimeSpan.FromSeconds(runtime.NextCheckDelay));
            }
        }));
}
