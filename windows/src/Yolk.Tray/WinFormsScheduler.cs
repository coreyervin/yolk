using Yolk.Core;

namespace Yolk.Tray;

/// <summary>
/// Runs the session's timers on the UI thread, so the session and AppModel
/// stay single-threaded.
/// </summary>
internal sealed class WinFormsScheduler : IScheduler
{
    public IDisposable Schedule(TimeSpan dueTime, TimeSpan? period, Action callback) =>
        new ScheduledCallback(dueTime, period, callback);

    private sealed class ScheduledCallback : IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer = new();
        private readonly TimeSpan? _period;
        private readonly Action _callback;
        // KillTimer leaves an already-posted WM_TIMER in the queue, so a
        // cancelled timer could still fire once without this.
        private bool _cancelled;

        public ScheduledCallback(TimeSpan dueTime, TimeSpan? period, Action callback)
        {
            _period = period;
            _callback = callback;
            _timer.Interval = Milliseconds(dueTime);
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_cancelled)
            {
                return;
            }
            if (_period is { } period)
            {
                _timer.Interval = Milliseconds(period);
            }
            else
            {
                _timer.Stop();
            }
            _callback();
        }

        public void Dispose()
        {
            _cancelled = true;
            _timer.Dispose();
        }

        // Timer.Interval is an int of milliseconds, which tops out near 24.8
        // days — well past the tray's longest "Stop after" of 8 hours.
        private static int Milliseconds(TimeSpan span) =>
            (int)Math.Clamp(span.TotalMilliseconds, 1, int.MaxValue);
    }
}
