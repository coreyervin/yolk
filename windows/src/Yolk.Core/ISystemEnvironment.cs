namespace Yolk.Core;

/// <summary>
/// The single boundary between the engine and the operating system — the
/// counterpart of <c>SystemEnvironment</c> on the Mac.
/// </summary>
/// <remarks>
/// <c>Win32Environment</c> is the real one; tests use a fake. Windows has no
/// equivalent of macOS's Accessibility grant, so the permission members the
/// Mac version carries have no counterpart here.
/// </remarks>
public interface ISystemEnvironment
{
    /// <summary>Seconds since the last keyboard or mouse input in this session.</summary>
    double IdleSeconds();

    ConsoleState GetConsoleState();

    /// <summary>
    /// Posts invisible synthetic input that resets the idle timer. False when
    /// the input could not be posted at all.
    /// </summary>
    bool PostActivity();

    /// <summary>The display-off timeout in seconds, or null when it is disabled.</summary>
    double? DisplaySleepSeconds();

    /// <summary>A clock that keeps counting through system sleep, in seconds.</summary>
    double MonotonicNow();

    DateTime Now();

    /// <exception cref="AssertionCreationException">The system refused the request.</exception>
    AssertionHandle CreateAssertion(string reason);

    void ReleaseAssertion(AssertionHandle handle);

    /// <summary>
    /// Blocks long enough for posted input to land, so the idle timer can be
    /// re-read to confirm it. Injected so tests never actually wait.
    /// </summary>
    void WaitForEventDelivery();
}

/// <summary>Opaque handle for a held power request, so fakes can vend their own.</summary>
public readonly record struct AssertionHandle(long Value);

public sealed class AssertionCreationException(int code)
    : Exception($"failed to create power request (error {code})")
{
    /// <summary>The system's error code, e.g. a Win32 error.</summary>
    public int Code { get; } = code;
}
