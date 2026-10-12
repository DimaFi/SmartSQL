using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class CsvMappingDialog : Window
{
    private readonly TableDefinition _table;
    private readonly string _content;
    private CsvImportSource _source;
    private readonly string _format;
    private readonly StackPanel _mappings = new() { Spacing = 8 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warning = new() { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _confirmOmissions = new() { Content = "Понимаю, что пропущенные столбцы не будут импортированы, а несопоставленные поля останутся пустыми." };
    private readonly List<MappingRow> _rows = [];
    private readonly Button _continue = new() { Content = "Проверить данные" };

    private sealed record FieldChoice(Guid? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record MappingRow(int SourceIndex, ComboBox Target);

    public CsvMappingDialog(TableDefinition table, string content, CsvImportSource source,
        string format = "CSV", bool allowDelimiter = true)
    {
        _table = table;
        _content = content;
        _source = source;
        _format = format;
        Title = $"Сопоставление {format} · SmartSQL";
        Width = 780;
        Height = 570;
        MinWidth = 630;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var delimiter = new ComboBox
        {
            ItemsSource = new[] { "Определить автоматически", "Точка с запятой (;)" , "Запятая (,)" },
            SelectedIndex = 0,
            MinWidth = 210
        };
        delimiter.SelectionChanged += (_, _) => ChangeDelimiter(delimiter.SelectedIndex);
        _confirmOmissions.IsVisible = false;
        _confirmOmissions.IsCheckedChanged += (_, _) => _error.Text = string.Empty;
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        _continue.Click += async (_, _) => await PrepareAsync();

        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10
        };
        layout.Children.Add(new TextBlock { Text = "Сопоставление столбцов", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var description = new TextBlock
        {
            Text = $"Выберите поле SmartSQL для каждого столбца {format}. После этого появится предварительный просмотр.",
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);
        var delimiterBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = "Разделитель", IsVisible = allowDelimiter, VerticalAlignment = VerticalAlignment.Center }, delimiter, _summary }
        };
        delimiter.IsVisible = allowDelimiter;
        Grid.SetRow(delimiterBar, 2);
        layout.Children.Add(delimiterBar);
        var scroll = new ScrollViewer { Content = _mappings };
        Grid.SetRow(scroll, 3);
        layout.Children.Add(scroll);
        var warnings = new StackPanel { Spacing = 6, Children = { _warning, _confirmOmissions } };
        Grid.SetRow(warnings, 4);
        layout.Children.Add(warnings);
        Grid.SetRow(_error, 5);
        layout.Children.Add(_error);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, _continue }
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
            _source = CsvTransfer.Inspect(_table, _content, delimiter);
            _error.Text = string.Empty;
            BuildRows();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
            _continue.IsEnabled = false;
        }
    }

    private void BuildRows()
    {
        _rows.Clear();
        _mappings.Children.Clear();
        _continue.IsEnabled = true;
        _summary.Text = _format == "CSV"
            ? $"Определено: {_source.Delimiter} · строк: {_source.RowCount}"
            : $"Строк: {_source.RowCount} · столбцов: {_source.Headers.Count}";
        var choices = new[] { new FieldChoice(null, "Не импортировать") }
            .Concat(_table.Fields.Select(field => new FieldChoice(field.Id, $"{field.Name} · {TypeLabel(field.Type)}"))).ToArray();
        var suggested = _source.SuggestMapping();
        for (var index = 0; index < _source.Headers.Count; index++)
        {
            var sourceIndex = index;
            var header = string.IsNullOrWhiteSpace(_source.Headers[index]) ? $"Столбец {index + 1}" : _source.Headers[index];
            var name = new TextBlock { Text = header, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var target = new ComboBox { ItemsSource = choices, MinWidth = 190 };
            target.SelectedItem = suggested.TryGetValue(index, out var fieldId)
                ? choices.First(choice => choice.Id == fieldId) : choices[0];
            target.SelectionChanged += (_, _) => UpdateWarnings();
            var example = new TextBlock
            {
                Text = _source.SampleRows.Count > 0 ? _source.SampleRows[0][index] : "",
                Opacity = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8 };
            Grid.SetColumn(target, 1);
            Grid.SetColumn(example, 2);
            row.Children.Add(name);
            row.Children.Add(target);
            row.Children.Add(example);
            _mappings.Children.Add(row);
            _rows.Add(new MappingRow(sourceIndex, target));
        }
        UpdateWarnings();
    }

    private Dictionary<int, Guid> GetMapping() => _rows
        .Where(row => ((FieldChoice?)row.Target.SelectedItem)?.Id is not null)
        .ToDictionary(row => row.SourceIndex, row => ((FieldChoice)row.Target.SelectedItem!).Id!.Value);

    private void UpdateWarnings()
    {
        var mapping = GetMapping();
        var skipped = Enumerable.Range(0, _source.Headers.Count).Where(index => !mapping.ContainsKey(index))
            .Select(index => string.IsNullOrWhiteSpace(_source.Headers[index]) ? $"Столбец {index + 1}" : _source.Headers[index]).ToArray();
        var missing = _table.Fields.Where(field => !mapping.Values.Contains(field.Id)).Select(field => field.Name).ToArray();
        var messages = new List<string>();
        if (skipped.Length > 0)
            messages.Add($"Не будут импортированы: {string.Join(", ", skipped)}.");
        if (missing.Length > 0)
            messages.Add($"Останутся пустыми: {string.Join(", ", missing)}.");
        _warning.Text = string.Join(" ", messages);
        _confirmOmissions.IsVisible = messages.Count > 0;
        _confirmOmissions.IsChecked = false;
    }

    private async Task PrepareAsync()
    {
        if (_confirmOmissions.IsVisible && _confirmOmissions.IsChecked != true)
        {
            _error.Text = "Подтвердите пропущенные столбцы и пустые поля.";
            return;
        }
        try
        {
            _continue.IsEnabled = false;
            _error.Text = "Проверка значений…";
            var source = _source;
            var mapping = GetMapping();
            var preview = await Task.Run(() => source.Prepare(mapping));
            Close(preview);
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
            _continue.IsEnabled = true;
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
