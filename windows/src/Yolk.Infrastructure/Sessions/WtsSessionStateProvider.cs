using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yolk.Application.Abstractions;
using Yolk.Domain.Sessions;
using Yolk.Infrastructure.Native;
using static Yolk.Infrastructure.Native.NativeMethods;

namespace Yolk.Infrastructure.Sessions;

/// <summary>Lock and connection state from Windows Terminal Services.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WtsSessionStateProvider : ISessionStateProvider
{
    public ConsoleState Current()
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
}
