using Yolk.Application.Abstractions;
using Yolk.Application.Sessions;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Features.ActivityCheck;

/// <summary>
/// One pass of the check loop, run on every timer tick: stop if the deadline
/// has passed, pause if the session is locked, and otherwise nudge once the
/// user has been idle long enough.
/// </summary>
public sealed record CheckActivity;

internal sealed class CheckActivityHandler(
    SessionRuntime runtime,
    IClock clock,
    ISessionStateProvider sessionState,
    IIdleTimeProvider idleTime,
    IInputSimulator input)
    : ICommandHandler<CheckActivity>
{
    public void Handle(CheckActivity command)
    {
        var session = runtime.Session;
        if (!session.IsRunning)
        {
            return;
        }

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
        runtime.Publish(new YolkEvent.Checked(idle));
        if (!session.ShouldNudge(idle))
        {
            return;
        }

        if (!input.PostActivity())
        {
            runtime.Publish(session.RecordPostFailed());
            return;
        }

        // Settle, then re-read idle to confirm the nudge actually landed.
        input.WaitForDelivery();
        runtime.Publish(session.RecordNudge(idleTime.IdleSeconds()));
    }
}
