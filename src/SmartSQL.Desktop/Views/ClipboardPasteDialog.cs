using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

/// <summary>Returns a validated preview; this dialog never changes the workspace.</summary>
public sealed class ClipboardPasteDialog : Window
{
    private readonly TableDefinition _table;
    private readonly TextBox _text = new() { AcceptsReturn = true, AcceptsTab = true, Height = 150, PlaceholderText = "Скопируйте диапазон в Excel и вставьте здесь (Ctrl+V)" };
    private readonly CheckBox _hasHeader = new() { Content = "Первая строка содержит заголовки", IsChecked = false };
    private readonly Button _inspect = new() { Content = "Разобрать ячейки" };
    private readonly Button _prepare = new() { Content = "Проверить данные", IsEnabled = false };
    private readonly StackPanel _mappings = new() { Spacing = 8 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warning = new() { Classes = { "warning" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _confirmOmissions = new() { Content = "Подтверждаю пропуск указанных столбцов и пустые поля.", IsVisible = false };
    private readonly List<MappingRow> _rows = [];
    private ClipboardImportSource? _source;

    private sealed record FieldChoice(Guid? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record MappingRow(int SourceIndex, ComboBox Target);

    public ClipboardPasteDialog(TableDefinition table, string? initialText = null)
    {
        _table = table;
        Title = "Вставка из Excel · SmartSQL";
        Width = 850;
        Height = 720;
        MinWidth = 650;
        MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _text.Text = initialText ?? string.Empty;
        _text.TextChanged += (_, _) => InvalidateSource();
        _hasHeader.IsCheckedChanged += (_, _) => InvalidateSource();
        _inspect.Click += (_, _) => Inspect();
        _prepare.Click += async (_, _) => await PrepareAsync();
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10
        };
        layout.Children.Add(new TextBlock { Text = "Вставить скопированные ячейки", FontSize = 24, FontWeight = FontWeight.SemiBold });
        Add(layout, new TextBlock
        {
            Text = $"Таблица: {table.Name}. Вставьте прямоугольный диапазон из Excel или другого табличного редактора. Записи будут добавлены после проверки и предварительного просмотра.",
            TextWrapping = TextWrapping.Wrap
        }, 1);
        Add(layout, _text, 2);
        Add(layout, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14,
            Children = { _hasHeader, _inspect }
        }, 3);
        Add(layout, _summary, 4);
        Add(layout, new ScrollViewer { Content = _mappings }, 5);
        Add(layout, new StackPanel { Spacing = 6, Children = { _warning, _confirmOmissions } }, 6);
        Add(layout, _error, 7);
        Add(layout, new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, _prepare }
        }, 8);
        Content = layout;
        if (!string.IsNullOrEmpty(initialText))
            Inspect();
    }

    private static void Add(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private void InvalidateSource()
    {
        _source = null;
        _rows.Clear();
        _mappings.Children.Clear();
        _prepare.IsEnabled = false;
        _summary.Text = "После изменения данных нажмите «Разобрать ячейки».";
        _warning.Text = string.Empty;
        _error.Text = string.Empty;
        _confirmOmissions.IsVisible = false;
        _confirmOmissions.IsChecked = false;
    }

    private void Inspect()
    {
        InvalidateSource();
        try
        {
            _source = ClipboardTableTransfer.Inspect(_table, _text.Text ?? string.Empty, _hasHeader.IsChecked == true);
            _summary.Text = $"Строк данных: {_source.RowCount:N0} · столбцов: {_source.ColumnCount}. Выберите поля таблицы для столбцов вставки.";
            var choices = new[] { new FieldChoice(null, "Не вставлять") }
                .Concat(_table.Fields.Select(field => new FieldChoice(field.Id, $"{field.Name} · {TypeLabel(field.Type)}"))).ToArray();
            var suggested = _source.SuggestMapping();
            for (var index = 0; index < _source.ColumnCount; index++)
            {
                var header = string.IsNullOrWhiteSpace(_source.Headers[index]) ? $"Столбец {index + 1}" : _source.Headers[index];
                var target = new ComboBox { ItemsSource = choices, MinWidth = 190 };
                target.SelectedItem = suggested.TryGetValue(index, out var fieldId)
                    ? choices.First(choice => choice.Id == fieldId) : choices[0];
                target.SelectionChanged += (_, _) => UpdateWarnings();
                var example = _source.SampleRows.Count > 0 ? _source.SampleRows[0][index] : string.Empty;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = header, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(target, 1);
                row.Children.Add(target);
                var sample = new TextBlock { Text = example, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(sample, 2);
                row.Children.Add(sample);
                _mappings.Children.Add(row);
                _rows.Add(new MappingRow(index, target));
            }
            _prepare.IsEnabled = _source.RowCount > 0;
            UpdateWarnings();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }

    private Dictionary<int, Guid> GetMapping() => _rows
        .Where(row => ((FieldChoice?)row.Target.SelectedItem)?.Id is not null)
        .ToDictionary(row => row.SourceIndex, row => ((FieldChoice)row.Target.SelectedItem!).Id!.Value);

    private void UpdateWarnings()
    {
        if (_source is null)
            return;
        var mapping = GetMapping();
        var skipped = Enumerable.Range(0, _source.ColumnCount).Where(index => !mapping.ContainsKey(index))
            .Select(index => string.IsNullOrWhiteSpace(_source.Headers[index]) ? $"Столбец {index + 1}" : _source.Headers[index]).ToArray();
        var empty = _table.Fields.Where(field => !mapping.Values.Contains(field.Id)).Select(field => field.Name).ToArray();
        var messages = new List<string>();
        if (skipped.Length > 0)
            messages.Add($"Не будут вставлены: {string.Join(", ", skipped)}.");
        if (empty.Length > 0)
            messages.Add($"Останутся пустыми: {string.Join(", ", empty)}.");
        _warning.Text = string.Join(" ", messages);
        _confirmOmissions.IsVisible = messages.Count > 0;
        _confirmOmissions.IsChecked = false;
        _error.Text = string.Empty;
    }

    private async Task PrepareAsync()
    {
        if (_source is null)
            return;
        if (_confirmOmissions.IsVisible && _confirmOmissions.IsChecked != true)
        {
            _error.Text = "Подтвердите пропущенные столбцы и пустые поля.";
            return;
        }
        try
        {
            SetInputEnabled(false);
            _error.Text = "Проверка всех значений…";
            var source = _source;
            var mapping = GetMapping();
            var preview = await Task.Run(() => source.Prepare(mapping));
            Close(preview);
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
            SetInputEnabled(true);
        }
    }

    private void SetInputEnabled(bool enabled)
    {
        _text.IsEnabled = enabled;
        _hasHeader.IsEnabled = enabled;
        _inspect.IsEnabled = enabled;
        _mappings.IsEnabled = enabled;
        _confirmOmissions.IsEnabled = enabled;
        _prepare.IsEnabled = enabled;
    }

    private static string TypeLabel(FieldType type) => type switch
    {
        FieldType.Text => "Текст", FieldType.Number => "Число", FieldType.Date => "Дата", FieldType.Boolean => "Да/Нет", _ => "?"
    };
}
