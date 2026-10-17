namespace Yolk.Application.Abstractions;

/// <summary>
/// Runs the engine's timers. Every callback must arrive on the thread that
/// owns the session — the CLI's event loop, or the tray app's UI thread — so
/// the session itself never needs a lock.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Calls <paramref name="callback"/> after <paramref name="dueTime"/>, then
    /// every <paramref name="period"/> if one is given. Disposing the result
    /// cancels it, including a callback that is already queued.
    /// </summary>
    IDisposable Schedule(TimeSpan dueTime, TimeSpan? period, Action callback);
}
