using System.Runtime.InteropServices;

namespace Yolk.Platform.Windows;

/// <summary>
/// The Win32 declarations yolk uses. Source-generated (LibraryImport) and
/// blittable throughout, so they work unchanged under NativeAOT.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // ── user32: idle time and synthetic input ──────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    internal const uint INPUT_MOUSE = 0;
    internal const uint MOUSEEVENTF_MOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // INPUT is a tagged union; MOUSEINPUT is its largest member on 64-bit
    // Windows, so declaring only that member gives the right size (40 bytes).
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    // ── kernel32: power requests (the IOPMAssertion equivalent) ────────────

    internal const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    internal const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

    // REASON_CONTEXT's union is 24 bytes on 64-bit Windows (its Detailed
    // variant). Only SimpleReasonString is used; the padding keeps the struct
    // the size Windows expects, so it never reads past the end.
    [StructLayout(LayoutKind.Sequential)]
    internal struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        public char* SimpleReasonString;
        private readonly nint _detailedPadding1;
        private readonly nint _detailedPadding2;
    }

    internal enum POWER_REQUEST_TYPE
    {
        PowerRequestDisplayRequired = 0,
        PowerRequestSystemRequired = 1,
    }

    internal static readonly nint INVALID_HANDLE_VALUE = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint PowerCreateRequest(REASON_CONTEXT* context);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PowerSetRequest(nint powerRequest, POWER_REQUEST_TYPE requestType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PowerClearRequest(nint powerRequest, POWER_REQUEST_TYPE requestType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(SYSTEM_POWER_STATUS* status);

    [LibraryImport("kernel32.dll")]
    internal static partial nint LocalFree(nint memory);

    // ── wtsapi32: session lock and connection state ────────────────────────

    internal const nint WTS_CURRENT_SERVER_HANDLE = 0;
    internal const uint WTS_CURRENT_SESSION = 0xFFFFFFFF;
    internal const int WTSSessionInfoEx = 25;

    // Offsets into WTSINFOEXW: a DWORD Level, then a union aligned to 8 bytes
    // (it contains LARGE_INTEGERs) whose WTSINFOEX_LEVEL1 starts with
    // SessionId, SessionState, SessionFlags. Read by offset rather than
    // declaring the whole struct, which is mostly fixed-size name buffers.
    internal const int WtsInfoExLevelOffset = 0;
    internal const int WtsInfoExSessionStateOffset = 12;
    internal const int WtsInfoExSessionFlagsOffset = 16;

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSQuerySessionInformation(
        nint hServer, uint sessionId, int wtsInfoClass, out nint buffer, out uint bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    internal static partial void WTSFreeMemory(nint memory);

    // ── powrprof: display-off timeout ──────────────────────────────────────

    internal static readonly Guid GUID_VIDEO_SUBGROUP = new("7516b95f-f776-4464-8c53-06167f40cc99");
    internal static readonly Guid GUID_VIDEO_POWERDOWN_TIMEOUT = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetActiveScheme(nint userRootPowerKey, Guid** activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerReadACValueIndex(
        nint rootPowerKey, Guid* schemeGuid, Guid* subGroup, Guid* setting, uint* value);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerReadDCValueIndex(
        nint rootPowerKey, Guid* schemeGuid, Guid* subGroup, Guid* setting, uint* value);
}
