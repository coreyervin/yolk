using System.Collections.Concurrent;
using Yolk.Core;

namespace Yolk.Cli;

/// <summary>
/// The CLI's main thread: a queue of work drained in order, so the session's
/// timers and Ctrl-C all run on one thread and the session needs no locks.
/// The counterpart of the Mac CLI's <c>dispatchMain()</c>.
/// </summary>
internal sealed class EventLoop : IScheduler, IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];

    /// <summary>Queues work for the loop thread. Ignored once the loop has quit.</summary>
    public void Post(Action action)
    {
        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
            // Quit already called; late timer ticks and signals are dropped.
        }
    }

    public IDisposable Schedule(TimeSpan dueTime, TimeSpan? period, Action callback)
    {
        var scheduled = new ScheduledCallback(this, callback);
        scheduled.Start(dueTime, period ?? Timeout.InfiniteTimeSpan);
        return scheduled;
    }

    /// <summary>Runs queued work on the calling thread until <see cref="Quit"/>.</summary>
    public void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public void Quit() => _queue.CompleteAdding();

    public void Dispose() => _queue.Dispose();

    /// <summary>
    /// A thread-pool timer that posts into the loop. Disposing it marks it
    /// cancelled on the loop thread, so a tick already sitting in the queue is
    /// skipped — otherwise a stale timeout could stop a reconfigured session.
    /// </summary>
    private sealed class ScheduledCallback(EventLoop loop, Action callback) : IDisposable
    {
        private Timer? _timer;
        private bool _cancelled;

        public void Start(TimeSpan dueTime, TimeSpan period) =>
            _timer = new Timer(_ => loop.Post(Fire), null, dueTime, period);

        // Runs on the loop thread, as does Dispose (the session calls it), so
        // _cancelled needs no synchronization.
        private void Fire()
        {
            if (!_cancelled)
            {
                callback();
            }
        }

        public void Dispose()
        {
            _cancelled = true;
            _timer?.Dispose();
        }
    }
}
