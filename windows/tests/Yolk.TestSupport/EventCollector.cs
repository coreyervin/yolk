using Yolk.Application.Sessions;
using Yolk.Domain.Sessions;

namespace Yolk.TestSupport;

/// <summary>Collects the session's events so tests can assert on them.</summary>
public sealed class EventCollector
{
    public List<YolkEvent> Events { get; } = [];

    public void Attach(ISessionEvents source) => source.Emitted += Events.Add;

    public int Count(YolkEvent e) => Events.Count(x => x == e);
}
