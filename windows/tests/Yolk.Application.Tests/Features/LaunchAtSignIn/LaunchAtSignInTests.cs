using Yolk.TestSupport;

namespace Yolk.Application.Tests.Features.LaunchAtSignIn;

public class LaunchAtSignInTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCurrentStateIsReadFromTheSystemNotStored(bool enabled)
    {
        using var host = new TestHost();
        host.Startup.Enabled = enabled;
        Assert.Equal(enabled, host.GetLaunchAtSignIn().Enabled);
    }

    [Fact]
    public void EnablingRegistersTheApp()
    {
        using var host = new TestHost();

        var status = host.SetLaunchAtSignIn(true);

        Assert.Equal(1, host.Startup.RegisterCalls);
        Assert.True(status.Enabled);
        Assert.Null(status.Problem);
    }

    [Fact]
    public void DisablingUnregistersTheApp()
    {
        using var host = new TestHost();
        host.Startup.Enabled = true;

        var status = host.SetLaunchAtSignIn(false);

        Assert.Equal(1, host.Startup.UnregisterCalls);
        Assert.False(status.Enabled);
    }

    [Fact]
    public void AnUnregistrableCopyIsDetectedUpFrontNotOnFailure()
    {
        using var host = new TestHost();
        host.Startup.Registrable = false;

        var status = host.GetLaunchAtSignIn();

        Assert.False(status.CanEnable);
        Assert.NotNull(status.Problem);
    }

    [Fact]
    public void EnablingAnUnregistrableCopyIsRefusedWithoutCallingTheSystem()
    {
        using var host = new TestHost();
        host.Startup.Registrable = false;

        var status = host.SetLaunchAtSignIn(true);

        Assert.Equal(0, host.Startup.RegisterCalls);
        Assert.False(status.Enabled);
    }

    [Fact]
    public void ARegistrationFailureReportsTheRealStateAndExplainsItself()
    {
        using var host = new TestHost();
        host.Startup.RegisterFailure = new UnauthorizedAccessException("denied");

        var status = host.SetLaunchAtSignIn(true);

        Assert.False(status.Enabled);
        Assert.Contains("could not be added", status.Problem);
    }

    [Fact]
    public void AnUnregistrationFailureLeavesItOnAndExplainsItself()
    {
        using var host = new TestHost();
        host.Startup.Enabled = true;
        host.Startup.UnregisterFailure = new IOException("busy");

        var status = host.SetLaunchAtSignIn(false);

        Assert.True(status.Enabled);
        Assert.Contains("could not be removed", status.Problem);
    }

    [Fact]
    public void ALaterSuccessClearsAnEarlierProblem()
    {
        using var host = new TestHost();
        host.Startup.RegisterFailure = new IOException("busy");
        Assert.NotNull(host.SetLaunchAtSignIn(true).Problem);

        host.Startup.RegisterFailure = null;
        var status = host.SetLaunchAtSignIn(true);

        Assert.Null(status.Problem);
        Assert.True(status.Enabled);
    }
}
