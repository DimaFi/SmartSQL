using Avalonia;

namespace SmartSQL.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WorkspacePaths.InitializeRuntime();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
