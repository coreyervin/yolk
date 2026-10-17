using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yolk.Core;
using static Yolk.Platform.Windows.NativeMethods;

namespace Yolk.Platform.Windows;

/// <summary>
/// The real Windows environment — the counterpart of the Mac's
/// <c>SystemEnvironment.live</c>.
/// </summary>
/// <remarks>
/// Deliberately free of decisions: each member fetches raw numbers and hands
/// them to <see cref="Win32Interpretation"/>, which is the part under test.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe class Win32Environment : ISystemEnvironment
{
    private readonly Lock _lock = new();
    private readonly Dictionary<long, PowerRequest> _requests = [];
    private long _nextHandle = 1;

    // The reason string must outlive the request it describes, so it is freed
    // only when the request is released.
    private sealed record PowerRequest(nint Handle, nint Reason);

    public double IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        // Unreadable reads as "just active", which never triggers a nudge —
        // the safe direction to be wrong in.
        return GetLastInputInfo(ref info)
            ? Win32Interpretation.IdleSeconds(unchecked((uint)Environment.TickCount), info.dwTime)
            : 0;
    }

    public ConsoleState GetConsoleState()
    {
        if (!WTSQuerySessionInformation(
                WTS_CURRENT_SERVER_HANDLE, WTS_CURRENT_SESSION, WTSSessionInfoEx,
                out var buffer, out var bytes))
        {
            return Win32Interpretation.ConsoleState(null, null);
        }
        try
        {
            var readable = bytes >= WtsInfoExSessionFlagsOffset + sizeof(int)
                && Marshal.ReadInt32(buffer, WtsInfoExLevelOffset) == 1;
            return readable
                ? Win32Interpretation.ConsoleState(
                    Marshal.ReadInt32(buffer, WtsInfoExSessionStateOffset),
                    Marshal.ReadInt32(buffer, WtsInfoExSessionFlagsOffset))
                : Win32Interpretation.ConsoleState(null, null);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    public bool PostActivity()
    {
        // A zero-distance relative move: the cursor stays put, but the input
        // counts as activity and resets the idle timer Slack reads. The
        // engine re-reads idle afterwards, so if Windows ever ignores it the
        // session reports PermissionMissing rather than failing silently.
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_MOVE },
        };
        return SendInput(1, &input, sizeof(INPUT)) == 1;
    }

    public double? DisplaySleepSeconds()
    {
        Guid* scheme;
        if (PowerGetActiveScheme(0, &scheme) != 0)
        {
            return null;
        }
        try
        {
            var subgroup = GUID_VIDEO_SUBGROUP;
            var setting = GUID_VIDEO_POWERDOWN_TIMEOUT;
            uint value;
            var status = default(SYSTEM_POWER_STATUS);
            // ACLineStatus 0 is "on battery"; 1 and 255 (unknown) read the
            // plugged-in setting, which is what desktops use.
            var onBattery = GetSystemPowerStatus(&status) && status.ACLineStatus == 0;
            var result = onBattery
                ? PowerReadDCValueIndex(0, scheme, &subgroup, &setting, &value)
                : PowerReadACValueIndex(0, scheme, &subgroup, &setting, &value);
            return Win32Interpretation.DisplaySleepSeconds(result == 0 ? value : null);
        }
        finally
        {
            LocalFree((nint)scheme);
        }
    }

    // GetTickCount64 keeps counting through sleep and hibernation, which is
    // what the timeout backstop needs — like CLOCK_MONOTONIC on the Mac.
    public double MonotonicNow() => Environment.TickCount64 / 1000.0;

    public DateTime Now() => DateTime.Now;

    public AssertionHandle CreateAssertion(string reason)
    {
        var reasonString = Marshal.StringToHGlobalUni(reason);
        var context = new REASON_CONTEXT
        {
            Version = POWER_REQUEST_CONTEXT_VERSION,
            Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            SimpleReasonString = (char*)reasonString,
        };

        var handle = PowerCreateRequest(&context);
        if (handle == INVALID_HANDLE_VALUE || handle == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            Marshal.FreeHGlobal(reasonString);
            throw new AssertionCreationException(error);
        }
        // SystemRequired, not DisplayRequired: like PreventUserIdleSystemSleep
        // on the Mac, the machine stays up while the display may still sleep.
        if (!PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
        {
            var error = Marshal.GetLastPInvokeError();
            CloseHandle(handle);
            Marshal.FreeHGlobal(reasonString);
            throw new AssertionCreationException(error);
        }

        lock (_lock)
        {
            var id = _nextHandle++;
            _requests[id] = new PowerRequest(handle, reasonString);
            return new AssertionHandle(id);
        }
    }

    public void ReleaseAssertion(AssertionHandle handle)
    {
        PowerRequest? request;
        lock (_lock)
        {
            if (!_requests.Remove(handle.Value, out request))
            {
                return;
            }
        }
        PowerClearRequest(request.Handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
        CloseHandle(request.Handle);
        Marshal.FreeHGlobal(request.Reason);
    }

    // SendInput queues the input for the raw input thread; 200ms matches the
    // Mac's settle time before re-reading the idle timer.
    public void WaitForEventDelivery() => Thread.Sleep(200);
}
