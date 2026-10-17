using Microsoft.Extensions.DependencyInjection;
using Yolk.Application.Abstractions;
using Yolk.Application.Features.ActivityCheck;
using Yolk.Application.Features.KeepAwake;
using Yolk.Application.Features.LaunchAtSignIn;
using Yolk.Application.Features.Settings;
using Yolk.Application.Features.Status;
using Yolk.Application.Sessions;

namespace Yolk.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers every slice's handler. The host must also register the ports
    /// (AddInfrastructure does) and an <see cref="IScheduler"/> for its thread.
    /// </summary>
    /// <remarks>
    /// Listed explicitly rather than discovered by reflection, which keeps the
    /// CLI's NativeAOT build happy and makes each slice's entry point findable.
    /// Everything is a singleton: there is one session per process, and the
    /// handlers hold no state of their own.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<SessionRuntime>();
        services.AddSingleton<ISessionEvents>(sp => sp.GetRequiredService<SessionRuntime>());

        // KeepAwake
        services.AddSingleton<SessionTimers>();
        services.AddSingleton<ICommandHandler<StartKeepAwake, StartKeepAwakeResult>, StartKeepAwakeHandler>();
        services.AddSingleton<ICommandHandler<StopKeepAwake>, StopKeepAwakeHandler>();
        services.AddSingleton<ICommandHandler<ReconfigureKeepAwake>, ReconfigureKeepAwakeHandler>();

        // ActivityCheck
        services.AddSingleton<ICommandHandler<CheckActivity>, CheckActivityHandler>();

        // Settings
        services.AddSingleton<SettingsReader>();
        services.AddSingleton<IQueryHandler<GetSettings, SettingsDto>, GetSettingsHandler>();
        services.AddSingleton<ICommandHandler<ApplyStoredSettings>, ApplyStoredSettingsHandler>();
        services.AddSingleton<ICommandHandler<UpdateSettings, SettingsDto>, UpdateSettingsHandler>();

        // LaunchAtSignIn
        services.AddSingleton<IQueryHandler<GetLaunchAtSignIn, LaunchAtSignInStatus>, GetLaunchAtSignInHandler>();
        services.AddSingleton<ICommandHandler<SetLaunchAtSignIn, LaunchAtSignInStatus>, SetLaunchAtSignInHandler>();

        // Status
        services.AddSingleton<IQueryHandler<GetStatus, StatusDto>, GetStatusHandler>();

        return services;
    }
}
