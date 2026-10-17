using Yolk.Application.Abstractions;
using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Sessions;

/// <summary>
/// Lets the presentation layer follow the session as it runs — the CLI prints
/// these events, the tray app redraws on them.
/// </summary>
public interface ISessionEvents
{
    event Action<YolkEvent>? Emitted;
}

/// <summary>
/// The one live session the slices share: the aggregate, the power request it
/// holds, and its timers.
/// </summary>
/// <remarks>
/// Shared kernel for the KeepAwake, ActivityCheck, Settings, and Status slices.
/// It holds state and the single way to end a run; the use cases themselves
/// live in the slices. Single-threaded, like everything that touches it.
/// </remarks>
internal sealed class SessionRuntime(IPowerRequestService power) : ISessionEvents
{
    /// <summary>Shown in <c>powercfg /requests</c> while Yolk holds the system awake.</summary>
    public const string PowerRequestReason = "yolk: keeping the PC awake while agents run";

    private IDisposable? _ticker;
    private IDisposable? _deadline;

    public KeepAwakeSession Session { get; } = new(YolkConfig.Default);

    public PowerRequestHandle? PowerRequest { get; set; }

    /// <summary>
    /// How long until the next check, as decided by the last one. Checks are
    /// timed to land when idle reaches the nudge point.
    /// </summary>
    public double NextCheckDelay { get; set; } = 1;

    public event Action<YolkEvent>? Emitted;

    public void Publish(YolkEvent? e)
    {
        if (e is not null)
        {
            Emitted?.Invoke(e);
        }
    }

    /// <summary>Swaps in the next check; the check timer is one-shot, rescheduled each time.</summary>
    public void ReplaceTicker(IDisposable ticker)
    {
        _ticker?.Dispose();
        _ticker = ticker;
    }

    public void ReplaceDeadline(IDisposable? deadline)
    {
        _deadline?.Dispose();
        _deadline = deadline;
    }

    /// <summary>
    /// Ends the run, whoever asked: the user, the deadline timer, or a tick
    /// that found the deadline passed. Timers stop and the power request is
    /// released before the Stopped event goes out.
    /// </summary>
    public void End(StopReason reason)
    {
        if (!Session.IsRunning)
        {
            return;
        }
        CancelTimers();
        if (PowerRequest is { } handle)
        {
            PowerRequest = null;
            power.Release(handle);
        }
        Publish(Session.Stop(reason));
    }

    private void CancelTimers()
    {
        _ticker?.Dispose();
        _ticker = null;
        _deadline?.Dispose();
        _deadline = null;
    }
}
