using Yolk.Core;

namespace Yolk.TestSupport;

/// <summary>Collects a session's events so tests can assert on them.</summary>
public sealed class EventCollector
{
    public List<YolkEvent> Events { get; } = [];

    public void Attach(YolkSession session) => session.Emitted += Events.Add;

    public int Count(YolkEvent e) => Events.Count(x => x == e);
}
