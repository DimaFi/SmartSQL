using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class FilterDialog : Window
{
    private readonly TableDefinition _table;
    private readonly StackPanel _conditions = new() { Spacing = 8 };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly List<ConditionRow> _rows = [];

    private sealed record FieldChoice(FieldDefinition Field)
    {
        public override string ToString() => Field.Name;
    }

    private sealed record OperatorChoice(FilterOperator Operator, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ConditionRow(ComboBox Field, ComboBox Operator, TextBox Value, Grid Container);

    public FilterDialog(TableDefinition table, IReadOnlyList<RecordFilter> current)
    {
        _table = table;
        Title = "Фильтры · SmartSQL";
        Width = 680;
        Height = 500;
        MinWidth = 580;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var add = new Button { Content = "+ Условие", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddCondition(null);
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var apply = new Button { Content = "Применить" };
        apply.Click += (_, _) => Apply();

        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 12
        };
        layout.Children.Add(new TextBlock { Text = "Фильтры записей", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var help = new TextBlock { Text = "Все условия должны выполняться одновременно.", Opacity = 0.7 };
        Grid.SetRow(help, 1);
        layout.Children.Add(help);
        var scroll = new ScrollViewer { Content = _conditions };
        Grid.SetRow(scroll, 2);
        layout.Children.Add(scroll);
        var tools = new StackPanel { Spacing = 6, Children = { add, _error } };
        Grid.SetRow(tools, 3);
        layout.Children.Add(tools);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, apply }
        };
        Grid.SetRow(actions, 4);
        layout.Children.Add(actions);
        Content = layout;

        if (current.Count == 0)
            AddCondition(null);
        else
            foreach (var filter in current)
                AddCondition(filter);
    }

    private void AddCondition(RecordFilter? current)
    {
        if (_rows.Count >= 20)
        {
            _error.Text = "Можно задать не более 20 условий.";
            return;
        }
        var fields = _table.Fields.Select(field => new FieldChoice(field)).ToArray();
        var fieldBox = new ComboBox { ItemsSource = fields, MinWidth = 145, SelectedIndex = 0 };
        if (current is not null)
            fieldBox.SelectedItem = fields.FirstOrDefault(choice => choice.Field.Id == current.FieldId) ?? fields[0];
        var operatorBox = new ComboBox { MinWidth = 145 };
        var valueBox = new TextBox { PlaceholderText = "Значение", MinWidth = 145 };
        var remove = new Button { Content = "×" };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(operatorBox, 1);
        Grid.SetColumn(valueBox, 2);
        Grid.SetColumn(remove, 3);
        grid.Children.Add(fieldBox);
        grid.Children.Add(operatorBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(remove);
        var row = new ConditionRow(fieldBox, operatorBox, valueBox, grid);
        fieldBox.SelectionChanged += (_, _) => SetOperators(row, null);
        operatorBox.SelectionChanged += (_, _) =>
            valueBox.IsEnabled = ((OperatorChoice?)operatorBox.SelectedItem)?.Operator is not (FilterOperator.IsEmpty or FilterOperator.IsNotEmpty);
        remove.Click += (_, _) =>
        {
            _rows.Remove(row);
            _conditions.Children.Remove(grid);
        };
        _rows.Add(row);
        _conditions.Children.Add(grid);
        SetOperators(row, current?.Operator);
        valueBox.Text = current?.Value;
    }

    private static void SetOperators(ConditionRow row, FilterOperator? preferred)
    {
        if (row.Field.SelectedItem is not FieldChoice field)
            return;
        var options = field.Field.Type switch
        {
            FieldType.Text => new[]
            {
                new OperatorChoice(FilterOperator.Contains, "Содержит"),
                new OperatorChoice(FilterOperator.Equals, "Равно"),
                new OperatorChoice(FilterOperator.IsEmpty, "Пусто"),
                new OperatorChoice(FilterOperator.IsNotEmpty, "Не пусто")
            },
            FieldType.Boolean =>
            [
                new OperatorChoice(FilterOperator.Equals, "Равно"),
                new OperatorChoice(FilterOperator.IsEmpty, "Пусто"),
                new OperatorChoice(FilterOperator.IsNotEmpty, "Не пусто")
            ],
            _ =>
            [
                new OperatorChoice(FilterOperator.Equals, "Равно"),
                new OperatorChoice(FilterOperator.GreaterThan, "Больше / позже"),
                new OperatorChoice(FilterOperator.LessThan, "Меньше / раньше"),
                new OperatorChoice(FilterOperator.IsEmpty, "Пусто"),
                new OperatorChoice(FilterOperator.IsNotEmpty, "Не пусто")
            ]
        };
        row.Operator.ItemsSource = options;
        row.Operator.SelectedItem = options.FirstOrDefault(option => option.Operator == preferred) ?? options[0];
    }

    private void Apply()
    {
        try
        {
            var filters = new List<RecordFilter>();
            foreach (var row in _rows)
            {
                var field = ((FieldChoice?)row.Field.SelectedItem)?.Field
                    ?? throw new ArgumentException("Выберите поле.");
                var operation = ((OperatorChoice?)row.Operator.SelectedItem)?.Operator
                    ?? throw new ArgumentException("Выберите условие.");
                var value = operation is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty ? null : row.Value.Text;
                if (value is not null && FieldValue.Normalize(field.Type, value) is null)
                    throw new ArgumentException($"Укажите значение для поля «{field.Name}».");
                filters.Add(new RecordFilter(field.Id, operation, value));
            }
            Close(filters.ToArray());
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }
}
