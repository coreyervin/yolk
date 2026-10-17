using Microsoft.Extensions.DependencyInjection;
using Yolk.Application;
using Yolk.Application.Abstractions;
using Yolk.Application.Features.ActivityCheck;
using Yolk.Application.Features.KeepAwake;
using Yolk.Application.Features.LaunchAtSignIn;
using Yolk.Application.Features.Settings;
using Yolk.Application.Features.Status;
using Yolk.Application.Sessions;
using Yolk.Domain.Configuration;

namespace Yolk.TestSupport;

/// <summary>
/// The real <c>AddApplication()</c> composition with every port faked — what
/// the CLI and tray app build, minus Windows. Tests go through the same
/// handler interfaces the presentation layer uses, so the wiring is tested too.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _services;

    public TestHost()
    {
        _services = new ServiceCollection()
            .AddApplication()
            .AddSingleton<IClock>(System)
            .AddSingleton<IIdleTimeProvider>(System)
            .AddSingleton<IInputSimulator>(System)
            .AddSingleton<ISessionStateProvider>(System)
            .AddSingleton<IDisplaySettingsProvider>(System)
            .AddSingleton<IPowerRequestService>(System)
            .AddSingleton<ISettingsRepository>(Settings)
            .AddSingleton<IStartupRegistration>(Startup)
            .AddSingleton<IScheduler>(Scheduler)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Events.Attach(Get<ISessionEvents>());
    }

    public FakeSystem System { get; } = new();
    public FakeSettingsRepository Settings { get; } = new();
    public FakeStartupRegistration Startup { get; } = new();
    public ManualScheduler Scheduler { get; } = new();
    public EventCollector Events { get; } = new();

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public object Get(Type service) => _services.GetRequiredService(service);

    // ── Slices, as the presentation layer calls them ───────────────────────

    public StartKeepAwakeResult Start(YolkConfig? config = null) =>
        Get<ICommandHandler<StartKeepAwake, StartKeepAwakeResult>>().Handle(new StartKeepAwake(config));

    public void Stop() => Get<ICommandHandler<StopKeepAwake>>().Handle(new StopKeepAwake());

    public void Reconfigure(YolkConfig config) =>
        Get<ICommandHandler<ReconfigureKeepAwake>>().Handle(new ReconfigureKeepAwake(config));

    /// <summary>One timer tick.</summary>
    public void Tick() => Get<ICommandHandler<CheckActivity>>().Handle(new CheckActivity());

    public StatusDto Status(DateTime? asOf = null) =>
        Get<IQueryHandler<GetStatus, StatusDto>>().Handle(new GetStatus(asOf ?? System.WallClock));

    public SettingsDto GetSettings() => Get<IQueryHandler<GetSettings, SettingsDto>>().Handle(new GetSettings());

    public void ApplyStoredSettings() => Get<ICommandHandler<ApplyStoredSettings>>().Handle(new ApplyStoredSettings());

    public SettingsDto UpdateSettings(double? interval = null, double? threshold = null, StopAfter? stopAfter = null) =>
        Get<ICommandHandler<UpdateSettings, SettingsDto>>().Handle(new UpdateSettings(interval, threshold, stopAfter));

    public LaunchAtSignInStatus GetLaunchAtSignIn() =>
        Get<IQueryHandler<GetLaunchAtSignIn, LaunchAtSignInStatus>>().Handle(new GetLaunchAtSignIn());

    public LaunchAtSignInStatus SetLaunchAtSignIn(bool enabled) =>
        Get<ICommandHandler<SetLaunchAtSignIn, LaunchAtSignInStatus>>().Handle(new SetLaunchAtSignIn(enabled));

    public void Dispose() => _services.Dispose();
}
