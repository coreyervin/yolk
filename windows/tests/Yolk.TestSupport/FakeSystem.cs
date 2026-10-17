using Yolk.Application.Abstractions;
using Yolk.Domain.Sessions;

namespace Yolk.TestSupport;

/// <summary>
/// A mutable stand-in for the machine, implementing every OS-facing port.
/// Tests set properties directly and then drive CheckActivity by hand — no
/// real timers, no waiting.
/// </summary>
public sealed class FakeSystem :
    IClock, IIdleTimeProvider, IInputSimulator, ISessionStateProvider, IDisplaySettingsProvider, IPowerRequestService,
    IRandomSource
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

    /// <summary>
    /// Where each nudge lands within the threshold range: 0 is the bottom,
    /// just under 1 the top. Pinned so tests know exactly when a nudge is due.
    /// </summary>
    public double RandomUnit { get; set; }
    public double? DisplaySleep { get; set; }
    public DateTime WallClock { get; set; } = new(2001, 9, 9, 1, 46, 40, DateTimeKind.Local);

    /// <summary>When set, Acquire throws this instead of granting a request.</summary>
    public PowerRequestRefusedException? PowerRefusal { get; set; }

    public int PostCount { get; private set; }
    public List<string> Acquired { get; } = [];
    public List<PowerRequestHandle> Released { get; } = [];

    // CheckActivity reads idle once to decide, then again after posting to
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

    public void WaitForDelivery() { }

    public ConsoleState Current() => Console;

    public double? DisplaySleepSeconds() => DisplaySleep;

    public DateTime Now() => WallClock;

    public double MonotonicSeconds() => Monotonic;

    public double NextUnit() => RandomUnit;

    public PowerRequestHandle Acquire(string reason)
    {
        if (PowerRefusal is { } refusal)
        {
            throw refusal;
        }
        Acquired.Add(reason);
        return new PowerRequestHandle(_nextHandle++);
    }

    public void Release(PowerRequestHandle handle) => Released.Add(handle);
}
