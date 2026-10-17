using System.Runtime.Versioning;
using Yolk.Application.Abstractions;
using Yolk.Infrastructure.Native;
using static Yolk.Infrastructure.Native.NativeMethods;

namespace Yolk.Infrastructure.Display;

/// <summary>
/// "Turn off my screen after" from the active power plan, for whichever power
/// source the machine is on now.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class PowerPlanDisplaySettings : IDisplaySettingsProvider
{
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
}
