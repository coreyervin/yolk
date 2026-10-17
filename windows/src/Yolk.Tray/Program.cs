namespace Yolk.Tray;

internal static class Program
{
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
        Application.Run(new TrayApplication());
    }
}
