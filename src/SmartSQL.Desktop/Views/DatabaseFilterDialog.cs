using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

/// <summary>Builds provider-neutral AND filters from provider-confirmed columns and operations.</summary>
public sealed class DatabaseFilterDialog : Window
{
    private readonly DatabaseQueryCapabilities _capabilities;
    private readonly StackPanel _conditions = new() { Spacing = 10 };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly Button _add = new() { Content = "+ Условие" };
    private readonly Button _apply = new() { Content = "Применить фильтры", Classes = { "primary" } };
    private readonly List<ConditionRow> _rows = [];

    private sealed record ColumnChoice(DatabaseQueryColumn Column)
    {
        public override string ToString() => $"{Column.Name} · {KindLabel(Column.ValueKind)}";
    }

    private sealed record OperatorChoice(DatabaseFilterOperator Operator, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class ConditionRow
    {
        public required ComboBox Column { get; init; }
        public required ComboBox Operator { get; init; }
        public required TextBox Value { get; init; }
        public required TextBlock Hint { get; init; }
        public required Border Container { get; init; }
    }

    public DatabaseFilterDialog(DatabaseQueryCapabilities capabilities, IReadOnlyList<DatabaseFilter> current)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(current);
        if (current.Count > DatabaseBrowseQuery.MaxFilters)
            throw new ArgumentException($"Можно объединить не более {DatabaseBrowseQuery.MaxFilters} условий.", nameof(current));
        _capabilities = capabilities;

        Classes.Add("dialog");
        Title = "Фильтры таблицы · SmartSQL";
        Width = 820;
        Height = 650;
        MinWidth = 600;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _add.Click += (_, _) => AddCondition(null);
        _apply.Click += (_, _) => Submit();
        var clear = new Button { Content = "Очистить все" };
        clear.Click += (_, _) => Close((IReadOnlyList<DatabaseFilter>)[]);
        var cancel = new Button { Content = "Отмена", Classes = { "quiet" } };
        cancel.Click += (_, _) => Close(null);
        AutomationProperties.SetName(_add, "Добавить условие фильтра");
        AutomationProperties.SetName(_apply, "Применить все условия фильтра");
        AutomationProperties.SetName(clear, "Очистить все фильтры");

        var diagnostics = capabilities.Diagnostics.Select(item => item.Message)
            .Concat(capabilities.Columns.Where(column => !string.IsNullOrWhiteSpace(column.Limitation))
                .Select(column => $"{column.Name}: {column.Limitation}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var explanation = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Фильтры по типам", Classes = { "pageTitle" } },
                new TextBlock
                {
                    Text = $"До {DatabaseBrowseQuery.MaxFilters} условий объединяются через AND и применяются ко всей таблице до разбиения на страницы. Значения передаются как данные, SQL вводить не нужно.",
                    Classes = { "muted" }, TextWrapping = TextWrapping.Wrap
                }
            }
        };
        if (diagnostics.Length > 0)
            explanation.Children.Add(new Expander
            {
                Header = "Ограничения провайдера",
                Content = new TextBlock { Text = string.Join("\n", diagnostics), TextWrapping = TextWrapping.Wrap }
            });

        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _add, clear })
        {
            button.Margin = new Thickness(0, 0, 8, 6);
            tools.Children.Add(button);
        }
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, _apply }
        };
        var layout = new Grid
        {
            Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), RowSpacing = 12
        };
        layout.Children.Add(explanation);
        Add(layout, new ScrollViewer
        {
            Content = _conditions,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        }, 1);
        Add(layout, tools, 2);
        Add(layout, _error, 3);
        Add(layout, actions, 4);
        Content = layout;

        if (current.Count == 0)
            AddCondition(null);
        else
            foreach (var filter in current)
                AddCondition(filter);
        UpdateAvailability();
    }

    private void AddCondition(DatabaseFilter? current)
    {
        if (_rows.Count >= DatabaseBrowseQuery.MaxFilters)
        {
            _error.Text = $"Можно задать не более {DatabaseBrowseQuery.MaxFilters} условий.";
            return;
        }

        current?.Validate();
        var columns = _capabilities.Columns.Where(column => column.Operators.Count > 0)
            .Select(column => new ColumnChoice(column)).ToArray();
        if (columns.Length == 0)
        {
            _error.Text = "Провайдер не подтвердил ни одного столбца для фильтрации.";
            UpdateAvailability();
            return;
        }

        var columnBox = new ComboBox { ItemsSource = columns, MinWidth = 135, SelectedIndex = 0 };
        if (current is not null)
        {
            columnBox.SelectedItem = columns.FirstOrDefault(choice => choice.Column.Name == current.ColumnName)
                ?? throw new ArgumentException($"Столбец фильтра «{current.ColumnName}» больше не доступен.", nameof(current));
            var confirmed = ((ColumnChoice)columnBox.SelectedItem).Column;
            if (!confirmed.Operators.Contains(current.Operator))
                throw new ArgumentException($"Операция фильтра больше не доступна для столбца «{current.ColumnName}».", nameof(current));
        }
        var operatorBox = new ComboBox { MinWidth = 130 };
        var valueBox = new TextBox { MinWidth = 150, MaxLength = DatabaseFilter.MaxValueCharacters };
        var hint = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        var remove = new Button { Content = "×", Width = 36, Padding = new Thickness(0) };
        AutomationProperties.SetName(columnBox, $"Столбец условия {_rows.Count + 1}");
        AutomationProperties.SetName(operatorBox, $"Операция условия {_rows.Count + 1}");
        AutomationProperties.SetName(valueBox, $"Значение условия {_rows.Count + 1}");
        AutomationProperties.SetName(remove, $"Удалить условие {_rows.Count + 1}");

        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,1.15*,1.5*,Auto"), ColumnSpacing = 8 };
        fields.Children.Add(columnBox);
        AddColumn(fields, operatorBox, 1);
        AddColumn(fields, valueBox, 2);
        AddColumn(fields, remove, 3);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 5 };
        body.Children.Add(fields);
        Add(body, hint, 1);
        var container = new Border
        {
            Child = body, Padding = new Thickness(10), CornerRadius = new CornerRadius(8), Classes = { "chip" }
        };
        var row = new ConditionRow
        {
            Column = columnBox, Operator = operatorBox, Value = valueBox, Hint = hint, Container = container
        };
        columnBox.SelectionChanged += (_, _) => SetOperators(row, null);
        operatorBox.SelectionChanged += (_, _) => UpdateValueEditor(row);
        remove.Click += (_, _) =>
        {
            _rows.Remove(row);
            _conditions.Children.Remove(container);
            _error.Text = string.Empty;
            UpdateAvailability();
        };
        _rows.Add(row);
        _conditions.Children.Add(container);
        SetOperators(row, current?.Operator);
        valueBox.Text = current?.Value ?? string.Empty;
        UpdateValueEditor(row);
        UpdateAvailability();
    }

    private static void SetOperators(ConditionRow row, DatabaseFilterOperator? preferred)
    {
        if (row.Column.SelectedItem is not ColumnChoice choice)
            return;
        var operations = choice.Column.Operators.Select(operation => new OperatorChoice(operation, OperatorLabel(operation))).ToArray();
        row.Operator.ItemsSource = operations;
        row.Operator.SelectedItem = operations.FirstOrDefault(option => option.Operator == preferred) ?? operations.FirstOrDefault();
        row.Hint.Text = TypeHint(choice.Column);
        ToolTip.SetTip(row.Column, choice.Column.Limitation ?? row.Hint.Text);
    }

    private static void UpdateValueEditor(ConditionRow row)
    {
        var nullTest = row.Operator.SelectedItem is OperatorChoice
        {
            Operator: DatabaseFilterOperator.IsNull or DatabaseFilterOperator.IsNotNull
        };
        row.Value.IsEnabled = !nullTest;
        row.Value.PlaceholderText = nullTest ? "Для NULL значение не вводится"
            : row.Column.SelectedItem is ColumnChoice choice ? ValueHint(choice.Column.ValueKind) : "Значение";
    }

    private void UpdateAvailability()
    {
        _add.IsEnabled = _capabilities.Columns.Any(column => column.Operators.Count > 0)
            && _rows.Count < DatabaseBrowseQuery.MaxFilters;
        _apply.IsEnabled = _rows.Count > 0;
    }

    private void Submit()
    {
        try
        {
            var filters = new List<DatabaseFilter>(_rows.Count);
            foreach (var row in _rows)
            {
                var column = (row.Column.SelectedItem as ColumnChoice)?.Column
                    ?? throw new ArgumentException("Выберите столбец фильтра.");
                var operation = (row.Operator.SelectedItem as OperatorChoice)?.Operator
                    ?? throw new ArgumentException($"Выберите операцию для столбца «{column.Name}».");
                if (!column.Operators.Contains(operation))
                    throw new ArgumentException($"Операция больше не доступна для столбца «{column.Name}».");
                var nullTest = operation is DatabaseFilterOperator.IsNull or DatabaseFilterOperator.IsNotNull;
                var value = nullTest ? null : row.Value.Text ?? string.Empty;
                if (!nullTest && column.ValueKind != DatabaseFilterValueKind.Text && string.IsNullOrEmpty(value))
                    throw new ArgumentException($"Введите значение для столбца «{column.Name}». Пустая строка разрешена только для текста; NULL задаётся отдельной операцией.");
                var filter = new DatabaseFilter(column.Name, operation, value);
                filter.Validate();
                filters.Add(filter);
            }
            var query = new DatabaseBrowseQuery(Filters: filters);
            query.Validate();
            Close((IReadOnlyList<DatabaseFilter>)filters.ToArray());
        }
        catch (ArgumentException exception)
        {
            _error.Text = exception.Message;
        }
    }

    private static string TypeHint(DatabaseQueryColumn column)
    {
        var hint = column.ValueKind switch
        {
            DatabaseFilterValueKind.Text => "Текст: пустая строка допустима и отличается от NULL.",
            DatabaseFilterValueKind.Integer => "Целое число: точный формат и диапазон проверит провайдер.",
            DatabaseFilterValueKind.Number => "Число: точность и формат проверит провайдер.",
            DatabaseFilterValueKind.Date => "Дата: используйте формат, указанный провайдером (обычно ГГГГ-ММ-ДД).",
            DatabaseFilterValueKind.Boolean => "Логическое значение: используйте true/false, если провайдер не указал иначе.",
            _ => "Для этого типа доступны только подтверждённые провайдером операции."
        };
        return string.IsNullOrWhiteSpace(column.Limitation) ? hint : $"{hint} {column.Limitation}";
    }

    private static string ValueHint(DatabaseFilterValueKind kind) => kind switch
    {
        DatabaseFilterValueKind.Text => "Текст, включая пустую строку",
        DatabaseFilterValueKind.Integer => "Целое число",
        DatabaseFilterValueKind.Number => "Число",
        DatabaseFilterValueKind.Date => "Дата",
        DatabaseFilterValueKind.Boolean => "true или false",
        _ => "Значение"
    };

    private static string KindLabel(DatabaseFilterValueKind kind) => kind switch
    {
        DatabaseFilterValueKind.Text => "текст",
        DatabaseFilterValueKind.Integer => "целое",
        DatabaseFilterValueKind.Number => "число",
        DatabaseFilterValueKind.Date => "дата",
        DatabaseFilterValueKind.Boolean => "да/нет",
        _ => "ограниченный тип"
    };

    private static string OperatorLabel(DatabaseFilterOperator operation) => operation switch
    {
        DatabaseFilterOperator.Equal => "Равно",
        DatabaseFilterOperator.NotEqual => "Не равно",
        DatabaseFilterOperator.LessThan => "Меньше / раньше",
        DatabaseFilterOperator.LessOrEqual => "Меньше или равно",
        DatabaseFilterOperator.GreaterThan => "Больше / позже",
        DatabaseFilterOperator.GreaterOrEqual => "Больше или равно",
        DatabaseFilterOperator.Contains => "Содержит",
        DatabaseFilterOperator.StartsWith => "Начинается с",
        DatabaseFilterOperator.EndsWith => "Заканчивается на",
        DatabaseFilterOperator.IsNull => "Это NULL",
        DatabaseFilterOperator.IsNotNull => "Не NULL",
        _ => operation.ToString()
    };

    private static void Add(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private static void AddColumn(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
