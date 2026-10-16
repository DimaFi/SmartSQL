using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

public sealed class RestoreBackupDialog : Window
{
    private readonly IDataWorkspace _workspace;
    private readonly string _sourcePath;
    private readonly TextBox _confirmation = new() { PlaceholderText = "Введите имя текущего файла" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "error" } };
    private readonly Button _restore = new() { Content = "Восстановить" };

    public RestoreBackupDialog(IDataWorkspace workspace, string sourcePath)
    {
        _workspace = workspace;
        _sourcePath = sourcePath;
        Title = "Восстановление базы · SmartSQL";
        Width = 560;
        Height = 390;
        MinWidth = 480;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var currentName = Path.GetFileName(workspace.FilePath);
        var sourceName = Path.GetFileName(sourcePath);
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        _restore.Click += async (_, _) => await RestoreAsync(currentName);
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Восстановить базу из копии", FontSize = 23, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = $"Источник: {sourceName}\nТекущая база: {currentName}",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Текущие таблицы и записи будут заменены содержимым выбранной копии. Перед заменой SmartSQL автоматически сохранит текущее состояние рядом с базой.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "warning" }
                },
                new TextBlock { Text = $"Для подтверждения введите имя текущего файла: {currentName}", TextWrapping = TextWrapping.Wrap },
                _confirmation,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, _restore }
                }
            }
        };
    }

    private async Task RestoreAsync(string expectedName)
    {
        if (!string.Equals(_confirmation.Text?.Trim(), expectedName, StringComparison.Ordinal))
        {
            _status.Text = $"Введите точное имя «{expectedName}».";
            return;
        }
        try
        {
            _restore.IsEnabled = false;
            _status.Classes.Set("error", false);
            _status.Classes.Set("warning", true);
            _status.Text = "Проверка копии и восстановление…";
            var result = await Task.Run(() => _workspace.RestoreBackup(_sourcePath));
            Close(result);
        }
        catch (Exception exception)
        {
            _status.Classes.Set("warning", false);
            _status.Classes.Set("error", true);
            _status.Text = exception.Message;
            _restore.IsEnabled = true;
        }
    }
}
