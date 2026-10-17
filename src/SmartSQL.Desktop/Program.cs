using Avalonia;

namespace SmartSQL.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WorkspacePaths.TryInitializeRuntime();
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { WorkspacePaths.Shutdown(); }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
