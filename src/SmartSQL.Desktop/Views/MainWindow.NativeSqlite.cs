using Avalonia.Interactivity;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow
{
    private void OnOpenNativeSqlite(object? sender, RoutedEventArgs e)
    {
        if (_transferInProgress)
            return;

        OpenNativeSqliteRequested?.Invoke();
    }
}
