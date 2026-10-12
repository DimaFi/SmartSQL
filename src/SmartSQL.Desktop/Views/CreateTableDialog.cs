using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class CreateTableDialog : Window
{
    private static readonly FieldTypeOption[] Types =
    [
        new("Текст", FieldType.Text),
        new("Число", FieldType.Number),
        new("Дата", FieldType.Date),
        new("Да / Нет", FieldType.Boolean)
    ];

    private readonly IDataWorkspace _workspace;
    private readonly TextBox _tableName = new() { PlaceholderText = "Например, Студенты" };
    private readonly StackPanel _fields = new() { Spacing = 8 };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly List<FieldRow> _rows = [];

    private sealed record FieldTypeOption(string Label, FieldType Type)
    {
        public override string ToString() => Label;
    }

    private sealed record FieldRow(TextBox Name, ComboBox Type, Grid Container);

    public CreateTableDialog(IDataWorkspace workspace)
    {
        _workspace = workspace;
        Title = "Новая таблица · SmartSQL";
        Width = 520;
        Height = 530;
        MinWidth = 440;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var addField = new Button { Content = "+ Добавить поле", HorizontalAlignment = HorizontalAlignment.Left };
        addField.Click += (_, _) => AddField();
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var create = new Button { Content = "Создать таблицу" };
        create.Click += (_, _) => CreateTable();

        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 12
        };
        layout.Children.Add(new TextBlock { Text = "Создать таблицу", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var namePanel = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Название" }, _tableName } };
        Grid.SetRow(namePanel, 1);
        layout.Children.Add(namePanel);
        var fieldsLabel = new TextBlock { Text = "Поля и типы данных", FontWeight = FontWeight.SemiBold };
        Grid.SetRow(fieldsLabel, 2);
        layout.Children.Add(fieldsLabel);
        var scroller = new ScrollViewer { Content = _fields };
        Grid.SetRow(scroller, 3);
        layout.Children.Add(scroller);
        var tools = new StackPanel { Spacing = 6, Children = { addField, _error } };
        Grid.SetRow(tools, 4);
        layout.Children.Add(tools);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, create }
        };
        Grid.SetRow(actions, 5);
        layout.Children.Add(actions);
        Content = layout;
        AddField();
    }

    private void AddField()
    {
        var name = new TextBox { PlaceholderText = "Название поля" };
        var type = new ComboBox { ItemsSource = Types, SelectedIndex = 0, MinWidth = 125 };
        var remove = new Button { Content = "×" };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(type, 1);
        Grid.SetColumn(remove, 2);
        row.Children.Add(name);
        row.Children.Add(type);
        row.Children.Add(remove);
        var controls = new FieldRow(name, type, row);
        remove.Click += (_, _) =>
        {
            if (_rows.Count <= 1)
                return;
            _rows.Remove(controls);
            _fields.Children.Remove(row);
        };
        _rows.Add(controls);
        _fields.Children.Add(row);
        name.Focus();
    }

    private void CreateTable()
    {
        try
        {
            var fields = _rows.Select(row => new FieldInput(row.Name.Text ?? string.Empty,
                ((FieldTypeOption?)row.Type.SelectedItem)?.Type ?? FieldType.Text)).ToArray();
            var table = _workspace.CreateTable(_tableName.Text ?? string.Empty, fields);
            Close(table);
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }
}
