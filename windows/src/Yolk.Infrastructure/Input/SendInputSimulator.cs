using System.Runtime.Versioning;
using Yolk.Application.Abstractions;
using static Yolk.Infrastructure.Native.NativeMethods;

namespace Yolk.Infrastructure.Input;

/// <summary>The nudge: a zero-distance mouse move through <c>SendInput</c>.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class SendInputSimulator : IInputSimulator
{
    public bool PostActivity()
    {
        // The cursor stays put, but the input counts as activity and resets
        // the idle timer. CheckActivity re-reads idle afterwards, so if Windows
        // ever ignores it the session warns rather than failing silently.
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_MOVE },
        };
        return SendInput(1, &input, sizeof(INPUT)) == 1;
    }

    // SendInput queues the input for the raw input thread; 200ms matches the
    // Mac's settle time before re-reading the idle timer.
    public void WaitForDelivery() => Thread.Sleep(200);
}
