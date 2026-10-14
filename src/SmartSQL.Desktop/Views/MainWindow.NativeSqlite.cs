using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartSQL.Infrastructure;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow
{
    private async void OnOpenNativeSqlite(object? sender, RoutedEventArgs e)
    {
        if (_connectionDialogOpen || _transferInProgress) return;
        _connectionDialogOpen = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Открыть SQLite для просмотра",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("SQLite") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] }, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath()
                ?? throw new InvalidOperationException("Для просмотра нужен локальный SQLite-файл.");
            var browserWindow = new DatabaseBrowserWindow(Path.GetFileName(path), "SQLite", path,
                async cancellationToken => await NativeSqliteDatabaseBrowser.OpenAsync(path, cancellationToken));
            browserWindow.Show(this);
        }
        catch
        {
            StatusText.Text = "Не удалось открыть SQLite для просмотра. Проверьте файл и права чтения.";
        }
        finally { _connectionDialogOpen = false; }
    }
}
