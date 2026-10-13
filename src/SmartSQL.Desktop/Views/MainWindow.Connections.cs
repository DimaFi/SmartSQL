using Avalonia.Interactivity;
using SmartSQL.Infrastructure;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow
{
    private bool _connectionDialogOpen;

    private async void OnOpenConnections(object? sender, RoutedEventArgs e)
    {
        if (_connectionDialogOpen || _transferInProgress) return;
        _connectionDialogOpen = true;
        try
        {
            var store = new JsonConnectionProfileStore(Path.Combine(WorkspacePaths.DataDirectory, "connections.json"));
            var request = await new ConnectionsDialog(store).ShowDialog<ConnectionRequest?>(this);
            if (request is null) return;
            var browserWindow = new DatabaseBrowserWindow(request.Profile, request.Password,
                async (profile, password, cancellationToken) =>
                    await PostgresDatabaseBrowser.OpenAsync(profile, password, cancellationToken));
            browserWindow.Show(this);
        }
        catch
        {
            StatusText.Text = "Не удалось открыть подключение. Проверьте локальные профили и параметры сервера.";
        }
        finally { _connectionDialogOpen = false; }
    }
}
