using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class EditSchemaDialog : Window
{
    private readonly IDataWorkspace _workspace;
    private readonly TableDefinition _table;
    private readonly TextBox _tableName;
    private readonly Dictionary<Guid, TextBox> _fieldNames = [];
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

    public EditSchemaDialog(IDataWorkspace workspace, TableDefinition table)
    {
        _workspace = workspace;
        _table = table;
        _tableName = new TextBox { Text = table.Name };
        Title = "Названия таблицы и полей · SmartSQL";
        Width = 520;
        Height = 540;
        MinWidth = 430;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var fields = new StackPanel { Spacing = 8 };
        foreach (var field in table.Fields)
        {
            var name = new TextBox { Text = field.Name };
            _fieldNames[field.Id] = name;
            fields.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children =
                {
                    new TextBlock { Text = TypeLabel(field.Type), Width = 70, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 },
                    name
                }
            });
            Grid.SetColumn(name, 1);
        }
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var save = new Button { Content = "Сохранить названия" };
        save.Click += (_, _) => Save();

        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 10
        };
        layout.Children.Add(new TextBlock { Text = "Названия и заголовки", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var description = new TextBlock
        {
            Text = "Переименование не изменяет значения и типы данных.",
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);
        var tablePanel = new StackPanel { Spacing = 5, Children = { new TextBlock { Text = "Название таблицы" }, _tableName } };
        Grid.SetRow(tablePanel, 2);
        layout.Children.Add(tablePanel);
        var scroll = new ScrollViewer { Content = fields };
        Grid.SetRow(scroll, 3);
        layout.Children.Add(scroll);
        Grid.SetRow(_error, 4);
        layout.Children.Add(_error);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save }
        };
        Grid.SetRow(actions, 5);
        layout.Children.Add(actions);
        Content = layout;
    }

    private void Save()
    {
        try
        {
            var names = _fieldNames.ToDictionary(item => item.Key, item => item.Value.Text ?? string.Empty);
            Close(_workspace.UpdateSchema(_table.Id, _tableName.Text ?? string.Empty, names));
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }

    private static string TypeLabel(FieldType type) => type switch
    {
        FieldType.Text => "Текст",
        FieldType.Number => "Число",
        FieldType.Date => "Дата",
        FieldType.Boolean => "Да/Нет",
        _ => "?"
    };
}
