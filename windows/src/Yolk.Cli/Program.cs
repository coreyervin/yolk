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
// This file is the composition root and nothing more: flags in, slices wired
// up, events out, signals, and the exit code. The work happens in
// Yolk.Application's KeepAwake and ActivityCheck slices.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Yolk.Application;
using Yolk.Application.Abstractions;
using Yolk.Application.Features.KeepAwake;
using Yolk.Application.Sessions;
using Yolk.Cli;
using Yolk.Domain.Sessions;
using Yolk.Infrastructure;

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

// ── Composition ────────────────────────────────────────────────────────────

using var loop = new EventLoop();
using var services = new ServiceCollection()
    .AddApplication()
    .AddInfrastructure()
    .AddSingleton<IScheduler>(loop)
    .BuildServiceProvider();

var clock = services.GetRequiredService<IClock>();
var renderer = new ConsoleRenderer(
    invocation.Verbose,
    invocation.InvokedBare,
    Environment.ProcessId,
    clock.Now,
    WriteOut,
    WriteErr);

services.GetRequiredService<ISessionEvents>().Emitted += e =>
{
    renderer.Handle(e);
    // The session never exits the process; the CLI does, once it has said why.
    if (e is YolkEvent.Stopped)
    {
        loop.Quit();
    }
};

var start = services.GetRequiredService<ICommandHandler<StartKeepAwake, StartKeepAwakeResult>>();
var stop = services.GetRequiredService<ICommandHandler<StopKeepAwake>>();

if (start.Handle(new StartKeepAwake(invocation.Config)) is StartKeepAwakeResult.PowerRequestRefused refused)
{
    return Die($"failed to create power request (Win32 error {refused.Code})");
}

// The tip and the Ctrl-C hint come last, after any warning the start emitted.
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
            stop.Handle(new StopKeepAwake());
        });
    });

using var sigint = Trap(PosixSignal.SIGINT, "stopped");
using var sigquit = Trap(PosixSignal.SIGQUIT, "stopped");
using var sigterm = Trap(PosixSignal.SIGTERM, "terminated");
using var sighup = Trap(PosixSignal.SIGHUP, "terminal closed");

loop.Run();
return 0;
