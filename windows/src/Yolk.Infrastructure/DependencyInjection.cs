using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Yolk.Application.Abstractions;
using Yolk.Infrastructure.Display;
using Yolk.Infrastructure.Input;
using Yolk.Infrastructure.Persistence;
using Yolk.Infrastructure.Power;
using Yolk.Infrastructure.Randomness;
using Yolk.Infrastructure.Sessions;
using Yolk.Infrastructure.Startup;
using Yolk.Infrastructure.Time;

namespace Yolk.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the Windows implementation of every port.</summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton<IPowerRequestService, Win32PowerRequestService>();
        services.AddSingleton<IIdleTimeProvider, LastInputIdleTimeProvider>();
        services.AddSingleton<IInputSimulator, SendInputSimulator>();
        services.AddSingleton<ISessionStateProvider, WtsSessionStateProvider>();
        services.AddSingleton<IDisplaySettingsProvider, PowerPlanDisplaySettings>();
        services.AddSingleton<ISettingsRepository, RegistrySettingsRepository>();
        services.AddSingleton<IStartupRegistration, RunKeyStartupRegistration>();
        return services;
    }
}
