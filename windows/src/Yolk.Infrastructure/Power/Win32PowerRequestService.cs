using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yolk.Application.Abstractions;
using static Yolk.Infrastructure.Native.NativeMethods;

namespace Yolk.Infrastructure.Power;

/// <summary>
/// Holds the system awake with a Windows power request — the counterpart of
/// the Mac's IOPMAssertion. Visible in <c>powercfg /requests</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class Win32PowerRequestService : IPowerRequestService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<long, PowerRequest> _requests = [];
    private long _nextHandle = 1;

    // The reason string must outlive the request it describes, so it is freed
    // only when the request is released.
    private sealed record PowerRequest(nint Handle, nint Reason);

    public PowerRequestHandle Acquire(string reason)
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
            throw new PowerRequestRefusedException(error);
        }
        // SystemRequired, not DisplayRequired: like PreventUserIdleSystemSleep
        // on the Mac, the machine stays up while the display may still sleep.
        if (!PowerSetRequest(handle, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
        {
            var error = Marshal.GetLastPInvokeError();
            CloseHandle(handle);
            Marshal.FreeHGlobal(reasonString);
            throw new PowerRequestRefusedException(error);
        }

        lock (_lock)
        {
            var id = _nextHandle++;
            _requests[id] = new PowerRequest(handle, reasonString);
            return new PowerRequestHandle(id);
        }
    }

    public void Release(PowerRequestHandle handle)
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
}
