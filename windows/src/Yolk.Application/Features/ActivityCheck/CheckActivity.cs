using Yolk.Application.Abstractions;
using Yolk.Application.Sessions;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Features.ActivityCheck;

/// <summary>
/// One pass of the check loop: stop if the deadline has passed, pause if the
/// session is locked, and otherwise nudge once the user has been idle until
/// the random nudge point. Leaves <see cref="SessionRuntime.NextCheckDelay"/>
/// set for when the next pass should run.
/// </summary>
public sealed record CheckActivity;

internal sealed class CheckActivityHandler(
    SessionRuntime runtime,
    IClock clock,
    ISessionStateProvider sessionState,
    IIdleTimeProvider idleTime,
    IInputSimulator input,
    IRandomSource random)
    : ICommandHandler<CheckActivity>
{
    public void Handle(CheckActivity command)
    {
        var session = runtime.Session;
        if (!session.IsRunning)
        {
            return;
        }
        // Unless something below says sooner, check again after the interval.
        runtime.NextCheckDelay = session.Config.Interval;

        if (session.IsPastDeadline(clock.MonotonicSeconds()))
        {
            runtime.End(StopReason.TimeoutReached);
            return;
        }

        // Fail safe: only an active, unlocked, connected session gets input.
        runtime.Publish(session.ObserveConsole(sessionState.Current()));
        if (session.IsPaused)
        {
            return;
        }

        var idle = idleTime.IdleSeconds();
        runtime.Publish(new YolkEvent.Checked(idle, session.NudgeTarget));
        if (!session.ShouldNudge(idle))
        {
            // Wake right when idle reaches the nudge point.
            runtime.NextCheckDelay = session.DelayUntilNudge(idle);
            return;
        }

        if (!input.PostActivity())
        {
            runtime.Publish(session.RecordPostFailed());
            return;
        }

        // Settle, then re-read idle to confirm the nudge actually landed.
        input.WaitForDelivery();
        runtime.Publish(session.RecordNudge(idleTime.IdleSeconds(), random.NextUnit()));
        // A landed nudge has just reset idle, so time the next from zero.
        runtime.NextCheckDelay = session.DelayUntilNudge(0);
    }
}
