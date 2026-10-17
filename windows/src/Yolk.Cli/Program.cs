// yolk — keep your PC awake and Slack active while agents run.
//
// Two mechanisms:
//   1. A Windows power request (PowerRequestSystemRequired) stops the machine
//      from idle-sleeping, like the Mac version's IOKit assertion.
//   2. When system idle time crosses a threshold, posts a zero-distance mouse
//      move. This resets the idle timer Slack reads to decide "away", without
//      the cursor ever moving.
//
// While the screen is locked, activity simulation pauses: locking is the
// "I actually walked away" gesture.
//
// All of that lives in Yolk.Core, which the tray app shares. This file is only
// the process: flags in, events out, signals, and the exit code.

using System.Runtime.InteropServices;
using System.Text;
using Yolk.Cli;
using Yolk.Core;
using Yolk.Platform.Windows;

// The banner and warnings use ✓, •, ⚠, and 💡.
Console.OutputEncoding = Encoding.UTF8;

static void WriteOut(string message) => Console.Out.WriteLine(message);

static void WriteErr(string message) => Console.Error.WriteLine(message);

static int Die(string message)
{
    WriteErr($"yolk: {message}\nTry 'yolk --help'.");
    return 1;
}

// ── Flags ──────────────────────────────────────────────────────────────────

Invocation invocation;
try
{
    switch (ArgumentParser.Parse(args))
    {
        case ParseOutcome.Help:
            WriteOut(Usage.Text);
            return 0;
        case ParseOutcome.Version:
            WriteOut(Usage.VersionLine);
            return 0;
        case ParseOutcome.Run run:
            invocation = run.Invocation;
            break;
        default:
            throw new InvalidOperationException("unhandled parse outcome");
    }
}
catch (ArgumentError error)
{
    return Die(error.Message);
}

// Help and version work anywhere, so the flag tests can run on any OS. Doing
// the actual job needs Windows.
if (!OperatingSystem.IsWindows())
{
    return Die("this build of yolk only runs on Windows");
}

// ── Wiring ─────────────────────────────────────────────────────────────────

var environment = new Win32Environment();
using var loop = new EventLoop();

var renderer = new ConsoleRenderer(
    invocation.Verbose,
    invocation.InvokedBare,
    Environment.ProcessId,
    environment.Now,
    WriteOut,
    WriteErr);

using var session = new YolkSession(invocation.Config, environment, loop);
session.Emitted += e =>
{
    renderer.Handle(e);
    // The engine never exits; the CLI does, once the session has said why.
    if (e is YolkEvent.Stopped)
    {
        loop.Quit();
    }
};

try
{
    session.Start();
}
catch (AssertionCreationException error)
{
    return Die($"failed to create power request (Win32 error {error.Code})");
}

// The tip and the Ctrl-C hint come last, after any warning Start() emitted.
renderer.RenderStartupTail();

// ── Signals ────────────────────────────────────────────────────────────────

// On Windows .NET maps these to console events: SIGINT is Ctrl-C, SIGQUIT is
// Ctrl-Break, SIGTERM is a shutdown or logoff, and SIGHUP is the console
// window closing. Each is handled on the loop thread so the session stays
// single-threaded.
PosixSignalRegistration Trap(PosixSignal signal, string reason) =>
    PosixSignalRegistration.Create(signal, context =>
    {
        context.Cancel = true;
        loop.Post(() =>
        {
            renderer.UserStopReason = reason;
            session.Stop();
        });
    });

using var sigint = Trap(PosixSignal.SIGINT, "stopped");
using var sigquit = Trap(PosixSignal.SIGQUIT, "stopped");
using var sigterm = Trap(PosixSignal.SIGTERM, "terminated");
using var sighup = Trap(PosixSignal.SIGHUP, "terminal closed");

loop.Run();
return 0;
