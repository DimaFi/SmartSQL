using Avalonia.Interactivity;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow
{
    private void OnOpenConnections(object? sender, RoutedEventArgs e)
    {
        if (_transferInProgress)
            return;

        OpenConnectionsRequested?.Invoke();
    }
}
