using Yolk.Core;

namespace Yolk.TestSupport;

/// <summary>
/// A mutable stand-in for the whole machine. Tests set properties directly
/// and then drive the session's Tick() by hand — no real timers, no waiting.
/// </summary>
public sealed class FakeSystem : ISystemEnvironment
{
    private bool _pendingVerification;
    private long _nextHandle = 1;

    public double Idle { get; set; }
    public ConsoleState Console { get; set; } = ConsoleState.Active;
    public bool PostSucceeds { get; set; } = true;

    /// <summary>
    /// What the verification re-read returns after a successful post. Below
    /// 1.0 means "the input landed"; above means it was ignored.
    /// </summary>
    public double IdleAfterPost { get; set; } = 0.2;

    public double Monotonic { get; set; }
    public double? DisplaySleep { get; set; }
    public DateTime WallClock { get; set; } = new(2001, 9, 9, 1, 46, 40, DateTimeKind.Local);

    /// <summary>When set, CreateAssertion throws this instead of vending a handle.</summary>
    public AssertionCreationException? AssertionFailure { get; set; }

    public int PostCount { get; private set; }
    public List<string> Created { get; } = [];
    public List<AssertionHandle> Released { get; } = [];

    // The session reads idle once to decide, then again after posting to
    // confirm the timer actually reset.
    public double IdleSeconds()
    {
        if (_pendingVerification)
        {
            _pendingVerification = false;
            return IdleAfterPost;
        }
        return Idle;
    }

    public ConsoleState GetConsoleState() => Console;

    public bool PostActivity()
    {
        if (!PostSucceeds)
        {
            return false;
        }
        PostCount++;
        _pendingVerification = true;
        return true;
    }

    public double? DisplaySleepSeconds() => DisplaySleep;

    public double MonotonicNow() => Monotonic;

    public DateTime Now() => WallClock;

    public AssertionHandle CreateAssertion(string reason)
    {
        if (AssertionFailure is { } failure)
        {
            throw failure;
        }
        Created.Add(reason);
        return new AssertionHandle(_nextHandle++);
    }

    public void ReleaseAssertion(AssertionHandle handle) => Released.Add(handle);

    public void WaitForEventDelivery() { }
}
