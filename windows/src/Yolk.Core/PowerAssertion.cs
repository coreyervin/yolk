namespace Yolk.Core;

/// <summary>
/// Owns one power request that stops idle sleep, released on dispose.
/// </summary>
/// <remarks>
/// Release is tied to the object rather than to a single shutdown path,
/// because the tray app starts and stops sessions repeatedly without quitting.
/// </remarks>
public sealed class PowerAssertion : IDisposable
{
    private readonly ISystemEnvironment _environment;
    private AssertionHandle? _handle;

    /// <exception cref="AssertionCreationException">The system refused the request.</exception>
    public PowerAssertion(string reason, ISystemEnvironment environment)
    {
        _environment = environment;
        _handle = environment.CreateAssertion(reason);
    }

    /// <summary>Safe to call more than once; only the first call reaches the system.</summary>
    public void Dispose()
    {
        if (_handle is not { } handle)
        {
            return;
        }
        // Cleared before the call so a re-entrant dispose can't double-release.
        _handle = null;
        _environment.ReleaseAssertion(handle);
    }
}
