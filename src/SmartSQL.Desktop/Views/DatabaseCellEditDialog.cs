using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed record DatabaseCellEditChoice(EditableDatabaseColumn Column, string? Value);

/// <summary>Collects and validates one explicit cell change without accessing a provider.</summary>
public sealed class DatabaseCellEditDialog : Window
{
    private readonly IReadOnlyList<DatabaseColumn> _displayColumns;
    private readonly IReadOnlyList<string?> _values;
    private readonly ComboBox _column = new();
    private readonly TextBlock _oldState = new() { Classes = { "caption" } };
    private readonly TextBox _oldValue = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        MinHeight = 72, MaxHeight = 140
    };
    private readonly TextBox _newValue = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        MinHeight = 72, MaxHeight = 160
    };
    private readonly CheckBox _setNull = new() { Content = "Сохранить NULL (отсутствующее значение)" };
    private readonly ComboBox _booleanValue = new()
    {
        MinWidth = 180, IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _typeHint = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Сохранить одну ячейку", Classes = { "primary" } };
    private string? _old;
    private bool _updating;

    private sealed record ColumnChoice(EditableDatabaseColumn Column)
    {
        public override string ToString() => $"{Column.Name} · {Column.NativeType}";
    }

    public DatabaseCellEditDialog(DatabaseEditCapabilities capabilities,
        IReadOnlyList<DatabaseColumn> displayColumns, IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(displayColumns);
        ArgumentNullException.ThrowIfNull(values);
        if (displayColumns.Count != values.Count)
            throw new ArgumentException("Строка не соответствует текущей структуре таблицы. Обновите страницу.", nameof(values));
        if (capabilities.Columns.Any(column => !displayColumns.Any(display =>
                string.Equals(display.Name, column.Name, StringComparison.Ordinal))))
            throw new ArgumentException("Правила редактирования не соответствуют текущей структуре таблицы. Обновите страницу.", nameof(capabilities));
        _displayColumns = displayColumns;
        _values = values;

        var choices = capabilities.Columns.OrderBy(column => column.Ordinal)
            .Select(column => new ColumnChoice(column)).ToArray();
        var firstEditable = choices.FirstOrDefault(choice => choice.Column.CanEdit);
        if (firstEditable is null)
            throw new ArgumentException("В этой таблице нет столбцов, доступных для изменения.", nameof(capabilities));

        Classes.Add("dialog");
        Title = "Изменить значение · SmartSQL";
        Width = 620;
        Height = 650;
        MinWidth = 480;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _column.ItemsSource = choices;
        _column.SelectedItem = firstEditable;
        _column.SelectionChanged += (_, _) => LoadColumn();
        _newValue.TextChanged += (_, _) => UpdatePreview();
        _booleanValue.SelectionChanged += (_, _) => UpdatePreview();
        _setNull.IsCheckedChanged += (_, _) =>
        {
            _newValue.IsEnabled = _column.SelectedItem is ColumnChoice { Column.CanEdit: true }
                && _setNull.IsChecked != true;
            UpdatePreview();
        };
        _save.Click += (_, _) => Submit();
        var cancel = new Button { Content = "Отмена", Classes = { "quiet" } };
        cancel.Click += (_, _) => Close(null);

        var form = new StackPanel { Spacing = 8 };
        form.Children.Add(Field("Столбец", _column));
        form.Children.Add(new TextBlock { Text = "Текущее значение" });
        form.Children.Add(_oldState);
        form.Children.Add(_oldValue);
        form.Children.Add(new TextBlock { Text = "Новое значение" });
        form.Children.Add(_newValue);
        form.Children.Add(_booleanValue);
        form.Children.Add(_setNull);
        form.Children.Add(_typeHint);
        form.Children.Add(new Border
        {
            Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(10), CornerRadius = new CornerRadius(8),
            Classes = { "chip" }, Child = _preview
        });
        form.Children.Add(_error);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, _save }
        };
        var layout = new Grid
        {
            Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 14
        };
        layout.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Изменение одной ячейки", Classes = { "pageTitle" } },
                new TextBlock
                {
                    Text = "Проверьте столбец, исходное и новое значение. Другие ячейки не изменятся.",
                    Classes = { "muted" }, TextWrapping = TextWrapping.Wrap
                }
            }
        });
        var scroll = new ScrollViewer
        {
            Content = form, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        Content = layout;
        LoadColumn();
    }

    private static StackPanel Field(string label, Control input) => new()
    {
        Spacing = 5, Children = { new TextBlock { Text = label }, input }
    };

    private EditableDatabaseColumn SelectedColumn =>
        (_column.SelectedItem as ColumnChoice)?.Column
        ?? throw new InvalidOperationException("Выберите столбец для изменения.");

    private void LoadColumn()
    {
        if (_column.SelectedItem is not ColumnChoice choice)
            return;
        _updating = true;
        try
        {
            var index = -1;
            for (var candidate = 0; candidate < _displayColumns.Count; candidate++)
                if (_displayColumns[candidate].Name == choice.Column.Name)
                {
                    index = candidate;
                    break;
                }
            _old = index >= 0 && index < _values.Count ? _values[index] : null;
            _oldValue.Text = _old ?? string.Empty;
            _oldState.Text = _old is null ? "NULL — значение отсутствует"
                : _old.Length == 0 ? "Пустая строка — значение существует, но не содержит символов"
                : "Полное текущее значение";
            _newValue.Text = _old ?? string.Empty;
            var isBoolean = choice.Column.Kind == DatabaseEditValueKind.Boolean;
            _newValue.IsVisible = !isBoolean;
            _booleanValue.IsVisible = isBoolean;
            _setNull.IsVisible = !isBoolean && choice.Column.IsNullable;
            _setNull.IsEnabled = choice.Column.CanEdit && choice.Column.IsNullable;
            _setNull.IsChecked = _old is null && choice.Column.IsNullable;
            _newValue.IsEnabled = choice.Column.CanEdit && _setNull.IsChecked != true;
            if (isBoolean)
            {
                var available = DatabaseBooleanChoices.ForColumn(choice.Column);
                _booleanValue.ItemsSource = available;
                var initial = DatabaseBooleanChoices.FromStored(_old);
                _booleanValue.SelectedItem = initial is not null && available.Contains(initial) ? initial : null;
                _booleanValue.IsEnabled = choice.Column.CanEdit;
                ToolTip.SetTip(_booleanValue, initial is null
                    ? "Исходное логическое значение не распознано; выберите новое явно."
                    : "Выберите Да, Нет или Не задано (NULL), если поле это допускает.");
            }
            _save.IsEnabled = choice.Column.CanEdit;
            _typeHint.Text = TypeHint(choice.Column);
            _error.Text = choice.Column.CanEdit
                ? string.Empty
                : choice.Column.ReadOnlyReason ?? "Этот столбец доступен только для чтения.";
        }
        finally
        {
            _updating = false;
        }
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_updating || _column.SelectedItem is not ColumnChoice choice)
            return;
        if (!choice.Column.CanEdit)
        {
            _preview.Text = $"Текущее значение: {Preview(_old)}";
            _error.Text = choice.Column.ReadOnlyReason ?? "Этот столбец доступен только для чтения.";
            return;
        }
        try
        {
            var normalized = NormalizedInput(choice.Column);
            _preview.Text = $"Будет изменено: {Preview(_old)}  →  {Preview(normalized)}";
            if (choice.Column.Kind != DatabaseEditValueKind.Boolean
                && normalized != _newValue.Text && normalized is not null)
                _preview.Text += $"\nНормализованное значение: {Preview(normalized)}";
            _error.Text = string.Empty;
            _save.IsEnabled = true;
        }
        catch (ArgumentException exception)
        {
            _preview.Text = $"Текущее значение: {Preview(_old)}";
            _error.Text = exception.Message;
            _save.IsEnabled = false;
        }
    }

    private void Submit()
    {
        try
        {
            var column = SelectedColumn;
            var normalized = NormalizedInput(column);
            _preview.Text = $"Будет изменено: {Preview(_old)}  →  {Preview(normalized)}";
            var oldBoolean = column.Kind == DatabaseEditValueKind.Boolean
                ? DatabaseBooleanChoices.FromStored(_old) : null;
            var unchanged = oldBoolean is not null
                ? string.Equals(oldBoolean.InputValue, normalized, StringComparison.Ordinal)
                : string.Equals(_old, normalized, StringComparison.Ordinal);
            if (unchanged)
            {
                _error.Text = "Значение не изменилось. Укажите новое значение или нажмите «Отмена».";
                return;
            }
            Close(new DatabaseCellEditChoice(column, normalized));
        }
        catch (ArgumentException exception)
        {
            _error.Text = exception.Message;
        }
    }

    private string? NormalizedInput(EditableDatabaseColumn column) =>
        column.Kind == DatabaseEditValueKind.Boolean
            ? DatabaseBooleanChoices.NormalizeChoice(column, _booleanValue.SelectedItem as DatabaseBooleanChoice)
            : DatabaseEditValueValidation.Normalize(column,
                _setNull.IsChecked == true ? null : _newValue.Text ?? string.Empty);

    private static string TypeHint(EditableDatabaseColumn column)
    {
        if (!column.CanEdit)
            return $"Тип СУБД: {column.NativeType} · только чтение: "
                + (column.ReadOnlyReason ?? "изменение этого столбца не поддерживается.");
        var input = column.Kind switch
        {
            DatabaseEditValueKind.Text when column.MaxTextCharacters is { } maximum =>
                $"текст, не более {maximum} символов; пробелы сохраняются",
            DatabaseEditValueKind.Text => "текст; пробелы и пустая строка сохраняются без изменений",
            DatabaseEditValueKind.Integer => "целое число; необязательный знак и цифры от 0 до 9",
            DatabaseEditValueKind.Decimal => "число с точкой; точность и экспонента сохраняются без округления",
            DatabaseEditValueKind.Date => "дата в формате ГГГГ-ММ-ДД",
            DatabaseEditValueKind.Boolean => column.IsNullable
                ? "логическое значение: выберите Да, Нет или Не задано (NULL)"
                : "логическое значение: выберите Да или Нет",
            _ => "редактируемое значение"
        };
        return $"Тип СУБД: {column.NativeType} · ввод: {input}.";
    }

    private static string Preview(string? value)
    {
        if (value is null)
            return "NULL";
        if (value.Length == 0)
            return "пустая строка «»";
        var visible = value.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return visible.Length <= 160 ? $"«{visible}»" : $"«{visible[..160]}…» ({visible.Length} знаков)";
    }
}
