using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
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
    private readonly StackPanel _fields = new() { Spacing = 8, Margin = new Thickness(0, 0, 18, 0) };
    private readonly TextBlock _error = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
    private readonly List<FieldRow> _rows = [];

    private sealed record FieldTypeOption(string Label, FieldType Type)
    {
        public override string ToString() => Label;
    }

    private sealed record FieldRow(TextBox Name, ComboBox Type, Grid Container,
        Button MoveUp, Button MoveDown, Button Remove);

    public CreateTableDialog(IDataWorkspace workspace)
    {
        _workspace = workspace;
        Classes.Add("dialog");
        Title = "Новая таблица · SmartSQL";
        Width = 660;
        Height = 560;
        MinWidth = 550;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var addField = new Button { Content = "+ Добавить поле", HorizontalAlignment = HorizontalAlignment.Left, Classes = { "secondary" } };
        addField.Click += (_, _) => AddField();
        var cancel = new Button { Content = "Отмена", Classes = { "quiet" } };
        cancel.Click += (_, _) => Close(null);
        var create = new Button { Content = "Создать таблицу", Classes = { "primary" } };
        create.Click += (_, _) => CreateTable();

        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 12
        };
        layout.Children.Add(new TextBlock { Text = "Создать таблицу", Classes = { "pageTitle" } });
        var namePanel = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Название" }, _tableName } };
        Grid.SetRow(namePanel, 1);
        layout.Children.Add(namePanel);
        var fieldsLabel = new TextBlock
        {
            Text = "Поля и типы данных · стрелками можно менять порядок",
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(fieldsLabel, 2);
        layout.Children.Add(fieldsLabel);
        var scroller = new ScrollViewer
        {
            Content = _fields,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
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
        var up = new Button { Content = "↑", Classes = { "iconButton" } };
        var down = new Button { Content = "↓", Classes = { "iconButton" } };
        var remove = new Button { Content = "×", Classes = { "iconButton", "danger" } };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 6
        };
        Grid.SetColumn(type, 1);
        Grid.SetColumn(up, 2);
        Grid.SetColumn(down, 3);
        Grid.SetColumn(remove, 4);
        row.Children.Add(name);
        row.Children.Add(type);
        row.Children.Add(up);
        row.Children.Add(down);
        row.Children.Add(remove);

        var controls = new FieldRow(name, type, row, up, down, remove);
        name.TextChanged += (_, _) => RefreshFieldActions();
        up.Click += (_, _) => MoveField(controls, -1);
        down.Click += (_, _) => MoveField(controls, 1);
        remove.Click += (_, _) => RemoveField(controls);

        _rows.Add(controls);
        _fields.Children.Add(row);
        RefreshFieldActions();
        name.Focus();
    }

    private void MoveField(FieldRow field, int direction)
    {
        var index = _rows.IndexOf(field);
        if (index < 0 || !FieldRowOrdering.MoveByOne(_rows, index, direction))
            return;

        // Keep the visual order and the fields passed to CreateTable in sync.
        // This is not a schema ALTER; the table has not yet been created.
        _fields.Children.RemoveAt(index);
        _fields.Children.Insert(index + direction, field.Container);
        RefreshFieldActions();
        field.Name.Focus();
    }

    private void RemoveField(FieldRow field)
    {
        if (_rows.Count <= 1)
            return;
        _rows.Remove(field);
        _fields.Children.Remove(field.Container);
        RefreshFieldActions();
    }

    private void RefreshFieldActions()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var field = _rows[i];
            field.MoveUp.IsEnabled = i > 0;
            field.MoveDown.IsEnabled = i < _rows.Count - 1;
            field.Remove.IsEnabled = _rows.Count > 1;

            var name = string.IsNullOrWhiteSpace(field.Name.Text)
                ? $"поле {i + 1}" : $"«{field.Name.Text}»";
            var moveUpLabel = $"Переместить {name} выше";
            var moveDownLabel = $"Переместить {name} ниже";
            var removeLabel = $"Удалить {name}";
            AutomationProperties.SetName(field.MoveUp, moveUpLabel);
            AutomationProperties.SetName(field.MoveDown, moveDownLabel);
            AutomationProperties.SetName(field.Remove, removeLabel);
            ToolTip.SetTip(field.MoveUp, moveUpLabel);
            ToolTip.SetTip(field.MoveDown, moveDownLabel);
            ToolTip.SetTip(field.Remove, removeLabel);
        }
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
