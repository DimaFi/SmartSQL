using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace SmartSQL.Desktop.Views;

public sealed record NativeSqliteOpenChoice(bool AllowEditing);

/// <summary>Chooses access for one existing SQLite connection without persisting or elevating it later.</summary>
public sealed class NativeSqliteOpenDialog : Window
{
    private readonly CheckBox _allowEditing = new()
    {
        Content = "Разрешить изменение ячеек в этом подключении",
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private readonly Button _openEditable = new()
    {
        Content = "Открыть с изменением ячеек",
        IsEnabled = false,
        Classes = { "secondary" }
    };

    public NativeSqliteOpenDialog(string path, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        path = Path.GetFullPath(path);
        backupDirectory = Path.GetFullPath(backupDirectory);
        var fileName = Path.GetFileName(path);

        Classes.Add("dialog");
        Title = "Открыть SQLite · SmartSQL";
        Width = 720;
        Height = 560;
        MinWidth = 500;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var readOnly = new Button { Content = "Открыть только для чтения", Classes = { "primary" } };
        var cancel = new Button { Content = "Отмена", Classes = { "quiet" } };
        readOnly.Click += (_, _) => Close(new NativeSqliteOpenChoice(false));
        _openEditable.Click += (_, _) =>
        {
            if (_allowEditing.IsChecked == true)
                Close(new NativeSqliteOpenChoice(true));
        };
        cancel.Click += (_, _) => Close(null);
        _allowEditing.IsCheckedChanged += (_, _) =>
            _openEditable.IsEnabled = _allowEditing.IsChecked == true;
        Opened += (_, _) => readOnly.Focus();
        Closed += (_, _) => _allowEditing.IsChecked = false;

        AutomationProperties.SetName(_allowEditing, "Разрешить изменение ячеек только для этого SQLite-подключения");
        AutomationProperties.SetName(readOnly, "Открыть SQLite только для чтения");
        AutomationProperties.SetName(_openEditable, "Открыть SQLite с разрешением изменения ячеек");
        AutomationProperties.SetName(cancel, "Отменить открытие SQLite");

        var pathText = new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap };
        var backupText = new TextBlock { Text = backupDirectory, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(pathText, $"Полный путь SQLite-файла: {path}");
        AutomationProperties.SetName(backupText, $"Каталог резервных копий: {backupDirectory}");
        ToolTip.SetTip(pathText, path);
        ToolTip.SetTip(backupText, backupDirectory);

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var button in new[] { cancel, _openEditable, readOnly })
        {
            button.Margin = new Thickness(8, 4, 0, 0);
            actions.Children.Add(button);
        }

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Как открыть SQLite-базу?",
                        Classes = { "pageTitle" },
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = fileName,
                        FontSize = 18,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    Field("Полный путь", pathText),
                    new TextBlock
                    {
                        Text = "Только чтение — рекомендуемый режим. Он не разрешает изменения файла.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap
                    },
                    _allowEditing,
                    new TextBlock
                    {
                        Text = "Перед каждой правкой обязательна резервная копия. Часть таблиц и полей останется только для чтения; добавление и удаление пока не поддерживаются. Разрешение действует только для этого подключения.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    Field("Каталог резервных копий", backupText),
                    new TextBlock
                    {
                        Text = "Просмотр списка таблиц не создаёт резервную копию. Она создаётся перед фактическим изменением.",
                        Classes = { "caption" },
                        TextWrapping = TextWrapping.Wrap
                    },
                    actions
                }
            }
        };
    }

    private static StackPanel Field(string label, Control value) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, Classes = { "caption" } },
            value
        }
    };
}
