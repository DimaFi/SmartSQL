using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class CsvNewTableDialog : Window
{
    private static readonly (string Label, FieldType Type)[] Types =
    [
        ("Текст", FieldType.Text),
        ("Число", FieldType.Number),
        ("Дата", FieldType.Date),
        ("Да / Нет", FieldType.Boolean)
    ];

    private readonly string _content;
    private CsvImportSource _source;
    private readonly TextBox _tableName;
    private readonly StackPanel _fieldRows = new() { Spacing = 8 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _import = new() { Content = "Создать и импортировать" };
    private readonly List<(TextBox Name, ComboBox Type)> _rows = [];

    private sealed record TypeChoice(string Label, FieldType Type)
    {
        public override string ToString() => Label;
    }

    public CsvNewTableDialog(string content, CsvImportSource source, string suggestedName)
    {
        _content = content;
        _source = source;
        _tableName = new TextBox { Text = suggestedName, PlaceholderText = "Название новой таблицы" };
        Title = "Новая таблица из CSV · SmartSQL";
        Width = 800;
        Height = 630;
        MinWidth = 620;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var delimiter = new ComboBox
        {
            ItemsSource = new[] { "Определить автоматически", "Точка с запятой (;)", "Запятая (,)" },
            SelectedIndex = 0,
            MinWidth = 210
        };
        delimiter.SelectionChanged += (_, _) => ChangeDelimiter(delimiter.SelectedIndex);
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        _import.Click += (_, _) => Prepare();

        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 10
        };
        layout.Children.Add(new TextBlock { Text = "Создать таблицу из CSV", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var description = new TextBlock
        {
            Text = "Заголовки станут полями. Проверьте их названия и типы: все строки будут проверены до создания таблицы. Пример значений показан справа.",
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);
        var namePanel = new StackPanel
        {
            Spacing = 5,
            Children = { new TextBlock { Text = "Название таблицы" }, _tableName }
        };
        Grid.SetRow(namePanel, 2);
        layout.Children.Add(namePanel);
        var delimiterPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { new TextBlock { Text = "Разделитель", VerticalAlignment = VerticalAlignment.Center }, delimiter, _summary }
        };
        Grid.SetRow(delimiterPanel, 3);
        layout.Children.Add(delimiterPanel);
        var scroll = new ScrollViewer { Content = _fieldRows };
        Grid.SetRow(scroll, 4);
        layout.Children.Add(scroll);
        Grid.SetRow(_error, 5);
        layout.Children.Add(_error);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, _import }
        };
        Grid.SetRow(actions, 6);
        layout.Children.Add(actions);
        Content = layout;
        BuildRows();
    }

    private void ChangeDelimiter(int selectedIndex)
    {
        try
        {
            var delimiter = selectedIndex switch { 1 => ';', 2 => ',', _ => (char?)null };
            _source = CsvTransfer.InspectNewTable(_content, delimiter);
            _error.Text = string.Empty;
            BuildRows();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
            _import.IsEnabled = false;
        }
    }

    private void BuildRows()
    {
        _rows.Clear();
        _fieldRows.Children.Clear();
        _import.IsEnabled = true;
        _summary.Text = $"Определено: {_source.Delimiter} · строк: {_source.RowCount}";
        var choices = Types.Select(type => new TypeChoice(type.Label, type.Type)).ToArray();
        for (var index = 0; index < _source.Headers.Count; index++)
        {
            var header = _source.Headers[index].Trim();
            var name = new TextBox { Text = header, PlaceholderText = $"Столбец {index + 1}" };
            var type = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 120 };
            var sample = new TextBlock
            {
                Text = _source.SampleRows.Count > 0 ? _source.SampleRows[0][index] : "",
                Opacity = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 10 };
            Grid.SetColumn(type, 1);
            Grid.SetColumn(sample, 2);
            row.Children.Add(name);
            row.Children.Add(type);
            row.Children.Add(sample);
            _fieldRows.Children.Add(row);
            _rows.Add((name, type));
        }
    }

    private void Prepare()
    {
        try
        {
            var fields = _rows.Select(row => new FieldInput(row.Name.Text ?? string.Empty,
                ((TypeChoice)row.Type.SelectedItem!).Type)).ToArray();
            var preview = _source.PrepareNewTable(_tableName.Text ?? string.Empty, fields);
            Close(preview);
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }
}
