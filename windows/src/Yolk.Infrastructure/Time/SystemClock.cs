using Yolk.Application.Abstractions;

namespace Yolk.Infrastructure.Time;

internal sealed class SystemClock : IClock
{
    public DateTime Now() => DateTime.Now;

    // GetTickCount64 keeps counting through sleep and hibernation, which is
    // what the timeout backstop needs — like CLOCK_MONOTONIC on the Mac.
    public double MonotonicSeconds() => Environment.TickCount64 / 1000.0;
}
