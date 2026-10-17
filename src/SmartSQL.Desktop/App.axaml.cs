using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SmartSQL.Desktop.Views;
using SmartSQL.Desktop.Presentation;

namespace SmartSQL.Desktop;

public partial class App : Avalonia.Application
{
    private string? _themeWarning;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemePreferences.Apply(this, ApplicationTheme.Gray);
        if (WorkspacePaths.IsReady) LoadStoredTheme();
    }

    private void LoadStoredTheme()
    {
        try { ThemePreferences.Apply(this, ThemePreferences.CreateDefault().Load()); }
        catch (Exception)
        {
            ThemePreferences.Apply(this, ApplicationTheme.Gray);
            _themeWarning = "Не удалось прочитать настройку темы. Включена серая тема; файл настройки сохранён без изменений.";
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = WorkspacePaths.IsReady ? new WorkspaceWindow(_themeWarning)
                : new StartupStorageWindow(WorkspacePaths.StartupError ?? "Хранилище не выбрано.", selected =>
                {
                    if (!WorkspacePaths.TryInitializeRuntime(selected)) return WorkspacePaths.StartupError;
                    LoadStoredTheme();
                    var previous = desktop.MainWindow;
                    var workspace = new WorkspaceWindow(_themeWarning);
                    desktop.MainWindow = workspace;
                    workspace.Show();
                    previous?.Close();
                    return null;
                });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
