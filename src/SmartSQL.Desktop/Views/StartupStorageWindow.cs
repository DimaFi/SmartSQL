using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace SmartSQL.Desktop.Views;

/// <summary>Shown before any workspace, preferences or default database is opened.</summary>
public sealed class StartupStorageWindow : Window
{
    private readonly Func<string?, string?> _retry;
    private readonly TextBox _reason;
    private bool _busy;
    private bool _transitioning;

    public StartupStorageWindow(string reason, Func<string?, string?> retry)
    {
        _retry = retry;
        Title = "Не удалось открыть хранилище · SmartSQL";
        Width = 720; Height = 490; MinWidth = 470; MinHeight = 370;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.Full;
        _reason = new TextBox
        {
            Text = reason, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 85, MaxHeight = 200
        };
        AutomationProperties.SetName(_reason, "Причина отказа запуска");
        var retryButton = new Button { Content = "Повторить обычный запуск", IsDefault = true };
        retryButton.Click += (_, _) => { if (!_busy) Retry(null); };
        var select = new Button { Content = "Выбрать существующее хранилище…" };
        select.Click += async (_, _) =>
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Папка ранее инициализированного хранилища SmartSQL", AllowMultiple = false
                });
                var path = folders.FirstOrDefault()?.TryGetLocalPath();
                if (path is not null) Retry(path);
                else if (folders.Count > 0) _reason.Text = "Выберите доступную локальную папку; виртуальное или облачное назначение не поддерживается.";
            }
            catch (Exception) { _reason.Text = "Не удалось выбрать локальную папку. Проверьте доступ и повторите попытку."; }
            finally { _busy = false; }
        };
        var exit = new Button { Content = "Выйти", IsCancel = true };
        exit.Click += (_, _) => { if (!_busy) Close(); };
        Closing += (_, e) => { if (_busy && !_transitioning) e.Cancel = true; };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { retryButton, select, exit } };
        foreach (var button in buttons.Children.OfType<Button>()) button.Margin = new Thickness(0, 0, 8, 8);
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Проверьте папку данных", FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Проект не открыт. SmartSQL не заменяет недоступное хранилище пустым проектом и не переносит прежние данные автоматически.", TextWrapping = TextWrapping.Wrap },
                    _reason,
                    new TextBlock { Text = "Проверьте доступ к папке и подключение диска. Если эта папка открыта в другом SmartSQL, сначала закройте то окно. Перенос старого хранилища пока требует отдельного этапа.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Выбор существующего хранилища действует только для этого запуска; нужна папка с действующим storage-root.json. Настройка обычного запуска не перезаписывается.", TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                    buttons
                }
            }
        };
    }

    private void Retry(string? existingRoot)
    {
        _transitioning = true;
        try
        {
            var error = _retry(existingRoot);
            if (error is not null) _reason.Text = error;
        }
        catch (Exception) { _reason.Text = "Не удалось завершить запуск. Существующий проект не заменён; проверьте папку данных."; }
        finally { _transitioning = false; }
    }
}
