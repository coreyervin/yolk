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

    private readonly List<IDisposable> _timers = [];

    public KeepAwakeSession Session { get; } = new(YolkConfig.Default);

    public PowerRequestHandle? PowerRequest { get; set; }

    public event Action<YolkEvent>? Emitted;

    public void Publish(YolkEvent? e)
    {
        if (e is not null)
        {
            Emitted?.Invoke(e);
        }
    }

    public void ReplaceTimers(params IDisposable[] timers)
    {
        CancelTimers();
        _timers.AddRange(timers);
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
        foreach (var timer in _timers)
        {
            timer.Dispose();
        }
        _timers.Clear();
    }
}
