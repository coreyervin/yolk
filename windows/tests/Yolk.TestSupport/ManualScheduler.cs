using Yolk.Application.Abstractions;

namespace Yolk.TestSupport;

/// <summary>
/// Records what was scheduled without ever running it. Tests drive
/// CheckActivity themselves, and can fire a timer by hand when that is the point.
/// </summary>
public sealed class ManualScheduler : IScheduler
{
    public sealed class Entry(TimeSpan dueTime, TimeSpan? period, Action callback) : IDisposable
    {
        public TimeSpan DueTime { get; } = dueTime;
        public TimeSpan? Period { get; } = period;
        public bool IsCancelled { get; private set; }

        public void Fire()
        {
            if (!IsCancelled)
            {
                callback();
            }
        }

        public void Dispose() => IsCancelled = true;
    }

    public List<Entry> Entries { get; } = [];

    public IEnumerable<Entry> Live => Entries.Where(e => !e.IsCancelled);

    public IDisposable Schedule(TimeSpan dueTime, TimeSpan? period, Action callback)
    {
        var entry = new Entry(dueTime, period, callback);
        Entries.Add(entry);
        return entry;
    }
}
