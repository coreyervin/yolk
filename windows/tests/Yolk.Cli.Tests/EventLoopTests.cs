using Yolk.Cli;

namespace Yolk.Cli.Tests;

public class EventLoopTests
{
    [Fact]
    public void RunsPostedWorkInOrderUntilQuit()
    {
        using var loop = new EventLoop();
        var log = new List<int>();
        loop.Post(() => log.Add(1));
        loop.Post(() => log.Add(2));
        loop.Post(loop.Quit);
        loop.Post(() => log.Add(3)); // queued before Quit ran, so still drained

        loop.Run();

        Assert.Equal([1, 2, 3], log);
    }

    [Fact]
    public void WorkPostedAfterQuitIsDropped()
    {
        using var loop = new EventLoop();
        loop.Quit();
        loop.Post(() => throw new InvalidOperationException("must not run"));
        loop.Run();
    }

    [Fact]
    public void AScheduledCallbackRunsOnTheLoopThread()
    {
        using var loop = new EventLoop();
        var loopThread = Environment.CurrentManagedThreadId;
        int? ranOn = null;
        using var timer = loop.Schedule(TimeSpan.FromMilliseconds(10), null, () =>
        {
            ranOn = Environment.CurrentManagedThreadId;
            loop.Quit();
        });

        loop.Run();

        Assert.Equal(loopThread, ranOn);
    }

    /// <summary>
    /// A tick can already be queued when its timer is cancelled — a stale
    /// timeout must not then stop a reconfigured session.
    /// </summary>
    [Fact]
    public void ACancelledCallbackAlreadyInTheQueueDoesNotRun()
    {
        using var loop = new EventLoop();
        var ran = false;
        IDisposable? timer = null;
        // Models the session mid-work on the loop thread while its timer
        // fires: the tick queues up behind this, then this cancels the timer.
        loop.Post(() =>
        {
            Thread.Sleep(200);
            timer!.Dispose();
            loop.Post(loop.Quit);
        });
        timer = loop.Schedule(TimeSpan.FromMilliseconds(1), null, () => ran = true);

        loop.Run();

        Assert.False(ran);
    }
}
