using Microsoft.Extensions.DependencyInjection;
using Yolk.Application;
using Yolk.Application.Abstractions;
using Yolk.Infrastructure;

namespace Yolk.Tray;

internal static class Program
{
    /// <summary>The composition root: wires the slices and the Windows adapters.</summary>
    [STAThread]
    private static void Main()
    {
        // One tray icon per user: a second launch (say, from the Start menu
        // while Yolk is already running) exits quietly.
        using var instance = new Mutex(initiallyOwned: true, @"Local\Yolk.Tray", out var isFirst);
        if (!isFirst)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        using var services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure()
            .AddSingleton<IScheduler, WinFormsScheduler>()
            .AddSingleton<TrayHandlers>()
            .AddSingleton<TrayApplication>()
            .BuildServiceProvider();

        // Qualified: inside Yolk.*, "Application" is the Yolk.Application namespace.
        System.Windows.Forms.Application.Run(services.GetRequiredService<TrayApplication>());
    }
}
