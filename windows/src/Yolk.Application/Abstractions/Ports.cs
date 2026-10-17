using Yolk.Domain.Configuration;
using Yolk.Domain.Sessions;

namespace Yolk.Application.Abstractions;

// The ports: everything the use cases need from the outside world, as narrow
// interfaces. Infrastructure implements them with Win32; tests use fakes.
// The Mac's single SystemEnvironment is split by role here, so each slice
// depends on only what it uses.

/// <summary>Wall-clock time, and a clock that keeps counting through sleep.</summary>
public interface IClock
{
    DateTime Now();

    /// <summary>Seconds on a clock that counts through system sleep.</summary>
    double MonotonicSeconds();
}

/// <summary>Where each nudge lands within the threshold range.</summary>
public interface IRandomSource
{
    /// <summary>A random number from 0 up to, but not including, 1.</summary>
    double NextUnit();
}

public interface IIdleTimeProvider
{
    /// <summary>Seconds since the last keyboard or mouse input in this session.</summary>
    double IdleSeconds();
}

public interface IInputSimulator
{
    /// <summary>
    /// Posts invisible input that resets the idle timer. False when the input
    /// could not be posted at all.
    /// </summary>
    bool PostActivity();

    /// <summary>
    /// Blocks long enough for posted input to land, so the idle timer can be
    /// re-read to confirm it.
    /// </summary>
    void WaitForDelivery();
}

public interface ISessionStateProvider
{
    /// <summary>Whether this login session is connected and unlocked.</summary>
    ConsoleState Current();
}

public interface IDisplaySettingsProvider
{
    /// <summary>Seconds until the display turns off, or null when it never does.</summary>
    double? DisplaySleepSeconds();
}

/// <summary>Holds the system awake — the Windows power request, or a fake.</summary>
public interface IPowerRequestService
{
    /// <exception cref="PowerRequestRefusedException">The system refused.</exception>
    PowerRequestHandle Acquire(string reason);

    /// <summary>Releasing a handle twice, or one never acquired, does nothing.</summary>
    void Release(PowerRequestHandle handle);
}

public readonly record struct PowerRequestHandle(long Value);

public sealed class PowerRequestRefusedException(int code)
    : Exception($"the power request was refused (error {code})")
{
    /// <summary>The system's error code, e.g. a Win32 error.</summary>
    public int Code { get; } = code;
}

/// <summary>Where the tray app's settings are kept between launches.</summary>
public interface ISettingsRepository
{
    /// <summary>
    /// The settings as stored. Any value may be missing or out of range — the
    /// store can be hand-edited — so callers validate before using them.
    /// </summary>
    StoredSettings Load();

    void Save(double interval, ThresholdRange threshold, StopAfter stopAfter);
}

/// <summary>
/// <paramref name="LegacyThreshold"/> is the single value stored before
/// thresholds became ranges, read so an upgrade keeps the user's setting.
/// </summary>
public sealed record StoredSettings(
    double? Interval,
    double? ThresholdMin,
    double? ThresholdMax,
    double? LegacyThreshold,
    StopAfter? StopAfter);

/// <summary>Whether Yolk starts when the user signs in.</summary>
public interface IStartupRegistration
{
    bool IsEnabled();

    /// <exception cref="IOException">Registration failed.</exception>
    /// <exception cref="UnauthorizedAccessException">Registration was denied.</exception>
    void Register();

    /// <exception cref="IOException">Unregistration failed.</exception>
    /// <exception cref="UnauthorizedAccessException">Unregistration was denied.</exception>
    void Unregister();

    /// <summary>Whether this copy of Yolk can be registered at all.</summary>
    bool CanRegister();
}
