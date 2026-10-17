using Yolk.Core;
using Yolk.Platform.Windows;

namespace Yolk.Core.Tests;

/// <summary>
/// The decisions Win32Environment delegates. These run on any OS; the Win32
/// calls themselves only run on a real Windows machine.
/// </summary>
public class Win32InterpretationTests
{
    private const int Active = Win32Interpretation.WtsActive;
    private const int Disconnected = 4; // WTSDisconnected
    private const int Locked = Win32Interpretation.WtsSessionStateLock;
    private const int Unlocked = Win32Interpretation.WtsSessionStateUnlock;
    private const int UnknownFlags = -1; // WTS_SESSIONSTATE_UNKNOWN

    [Fact]
    public void IdleIsTheTickDifferenceInSeconds() =>
        Assert.Equal(12.5, Win32Interpretation.IdleSeconds(tickCountNow: 100_000, lastInputTick: 87_500));

    [Fact]
    public void IdleStaysCorrectAcrossTheTickCounterWrap() =>
        // 2s before the 32-bit wrap, read 3s after it.
        Assert.Equal(5, Win32Interpretation.IdleSeconds(tickCountNow: 3_000, lastInputTick: uint.MaxValue - 1_999));

    [Fact]
    public void AConnectedUnlockedSessionIsActive() =>
        Assert.Equal(ConsoleState.Active, Win32Interpretation.ConsoleState(Active, Unlocked));

    [Fact]
    public void ALockedSessionIsLocked() =>
        Assert.Equal(ConsoleState.Locked, Win32Interpretation.ConsoleState(Active, Locked));

    [Fact]
    public void ADisconnectedSessionIsSwitchedOut() =>
        Assert.Equal(ConsoleState.SwitchedOut, Win32Interpretation.ConsoleState(Disconnected, Unlocked));

    [Fact]
    public void LockTakesPrecedenceOverDisconnected() =>
        Assert.Equal(ConsoleState.Locked, Win32Interpretation.ConsoleState(Disconnected, Locked));

    [Fact]
    public void AnUnreadableSessionFailsSafeToUnknown() =>
        Assert.Equal(ConsoleState.Unknown, Win32Interpretation.ConsoleState(null, null));

    [Fact]
    public void AnUnknownLockStateFailsSafeRatherThanAssumingActive() =>
        Assert.Equal(ConsoleState.Unknown, Win32Interpretation.ConsoleState(Active, UnknownFlags));

    [Fact]
    public void DisplaySleepIsReportedInSeconds() =>
        Assert.Equal(600, Win32Interpretation.DisplaySleepSeconds(600));

    [Fact]
    public void ADisplayThatNeverTurnsOffReportsNoSleep() =>
        Assert.Null(Win32Interpretation.DisplaySleepSeconds(0));

    [Fact]
    public void AnUnreadablePowerPlanReportsNoSleep() =>
        Assert.Null(Win32Interpretation.DisplaySleepSeconds(null));
}
