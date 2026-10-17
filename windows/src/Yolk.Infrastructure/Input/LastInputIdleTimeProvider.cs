using System.Runtime.Versioning;
using Yolk.Application.Abstractions;
using Yolk.Infrastructure.Native;
using static Yolk.Infrastructure.Native.NativeMethods;

namespace Yolk.Infrastructure.Input;

/// <summary>Idle time from <c>GetLastInputInfo</c>, the timer Slack reads.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class LastInputIdleTimeProvider : IIdleTimeProvider
{
    public double IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        // Unreadable reads as "just active", which never triggers a nudge —
        // the safe direction to be wrong in.
        return GetLastInputInfo(ref info)
            ? Win32Interpretation.IdleSeconds(unchecked((uint)Environment.TickCount), info.dwTime)
            : 0;
    }
}
