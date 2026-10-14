using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

public sealed record DatabaseInsertChoice(IReadOnlyList<DatabaseInsertValue> Values);

/// <summary>Collects and validates one native row without accessing a provider.</summary>
public sealed class DatabaseInsertDialog : Window
{
    private readonly DatabaseInsertCapabilities _capabilities;
    private readonly List<FieldInput> _fields = [];
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Добавить одну запись", Classes = { "primary" } };
    private bool _updating;

    private sealed record ModeChoice(DatabaseInsertValueMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class FieldInput
    {
        public required InsertableDatabaseColumn Metadata { get; init; }
        public required ComboBox Mode { get; init; }
        public required TextBox Value { get; init; }
        public required TextBlock Error { get; init; }
    }

    public DatabaseInsertDialog(DatabaseInsertCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!capabilities.CanInsert || capabilities.ConfirmationHandle is not { } handle || handle == Guid.Empty)
            throw new ArgumentException("Обновите структуру таблицы перед добавлением записи.", nameof(capabilities));
        if (capabilities.Columns.Count > DatabaseInsertRequest.MaxFields)
            throw new ArgumentException("В таблице слишком много полей для добавления одной записи.", nameof(capabilities));
        if (capabilities.Columns.Select(column => column.Column.Name).Distinct(StringComparer.Ordinal).Count()
            != capabilities.Columns.Count)
            throw new ArgumentException("Метаданные содержат повторяющиеся поля. Обновите структуру таблицы.", nameof(capabilities));
        _capabilities = capabilities;

        Classes.Add("dialog");
        Title = "Новая запись · SmartSQL";
        Width = 760;
        Height = 760;
        MinWidth = 540;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var fieldsPanel = new StackPanel { Spacing = 10 };
        foreach (var column in capabilities.Columns.OrderBy(column => column.Column.Ordinal))
            fieldsPanel.Children.Add(CreateField(column));

        _save.Click += (_, _) => Submit();
        var cancel = new Button { Content = "Отмена", Classes = { "quiet" } };
        cancel.Click += (_, _) => Close(null);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, _save }
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Для каждого поля выберите точное значение, явный NULL или заполнение сервером.",
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(fieldsPanel);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
            Classes = { "chip" }, Child = _preview
        });
        content.Children.Add(_error);

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
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
                new TextBlock { Text = "Добавление одной записи", Classes = { "pageTitle" } },
                new TextBlock
                {
                    Text = "Проверьте все поля и итог перед сохранением. Запись выполняется одной операцией.",
                    Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
                }
            }
        });
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        Content = layout;
        UpdatePreview();
    }

    private Control CreateField(InsertableDatabaseColumn column)
    {
        var modes = new List<ModeChoice>();
        if (column.CanUseDefault)
            modes.Add(new ModeChoice(DatabaseInsertValueMode.ServerDefault, "Заполнит сервер"));
        if (!column.MustUseDefault && column.Column.CanEdit)
        {
            if (column.Column.IsNullable)
                modes.Add(new ModeChoice(DatabaseInsertValueMode.Null, "NULL — нет значения"));
            modes.Add(new ModeChoice(DatabaseInsertValueMode.Value, "Указать значение"));
        }
        if (modes.Count == 0)
            throw new ArgumentException($"Для поля «{column.Column.Name}» нет допустимого способа заполнения.", nameof(_capabilities));

        var mode = new ComboBox { ItemsSource = modes, MinWidth = 190 };
        mode.SelectedItem = modes.FirstOrDefault(choice => choice.Mode == DatabaseInsertValueMode.ServerDefault)
            ?? modes.FirstOrDefault(choice => choice.Mode == DatabaseInsertValueMode.Null)
            ?? modes[0];
        mode.IsEnabled = modes.Count > 1;
        var value = new TextBox
        {
            AcceptsReturn = column.Column.Kind == DatabaseEditValueKind.Text,
            TextWrapping = TextWrapping.Wrap, MinHeight = 38,
            MaxHeight = column.Column.Kind == DatabaseEditValueKind.Text ? 120 : 52,
            IsEnabled = (mode.SelectedItem as ModeChoice)?.Mode == DatabaseInsertValueMode.Value
        };
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        var input = new FieldInput { Metadata = column, Mode = mode, Value = value, Error = error };
        _fields.Add(input);
        mode.SelectionChanged += (_, _) =>
        {
            value.IsEnabled = (mode.SelectedItem as ModeChoice)?.Mode == DatabaseInsertValueMode.Value;
            UpdatePreview();
        };
        value.TextChanged += (_, _) => UpdatePreview();

        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        title.Children.Add(new TextBlock
        {
            Text = column.Column.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        var nativeType = new TextBlock { Text = column.Column.NativeType, Classes = { "caption" } };
        Grid.SetColumn(nativeType, 1);
        title.Children.Add(nativeType);
        var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        controls.Children.Add(mode);
        Grid.SetColumn(value, 1);
        controls.Children.Add(value);
        var hint = new TextBlock
        {
            Text = FieldHint(column), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
        };
        return new Border
        {
            Classes = { "card" }, Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
            Child = new StackPanel { Spacing = 6, Children = { title, controls, hint, error } }
        };
    }

    private void UpdatePreview()
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            var values = CollectValues(out var firstError);
            _save.IsEnabled = firstError is null;
            _error.Text = firstError ?? string.Empty;
            _preview.Text = firstError is not null
                ? "Предпросмотр появится после исправления отмеченных полей."
                : values.Count == 0
                ? "Предпросмотр: запись без явных полей; все значения заполнит сервер."
                : "Предпросмотр новой записи:\n" + string.Join("\n", values.Select(Preview));
        }
        finally
        {
            _updating = false;
        }
    }

    private IReadOnlyList<DatabaseInsertValue> CollectValues(out string? firstError)
    {
        firstError = null;
        var values = new List<DatabaseInsertValue>(_fields.Count);
        foreach (var field in _fields)
        {
            field.Error.Text = string.Empty;
            try
            {
                var mode = (field.Mode.SelectedItem as ModeChoice)?.Mode
                    ?? throw new ArgumentException("Выберите способ заполнения поля.");
                var input = new DatabaseInsertValue(field.Metadata.Column.Name, mode,
                    mode == DatabaseInsertValueMode.Value ? field.Value.Text ?? string.Empty : null);
                values.Add(DatabaseInsertValueValidation.Normalize(field.Metadata, input));
            }
            catch (ArgumentException exception)
            {
                field.Error.Text = exception.Message;
                firstError ??= $"Поле «{field.Metadata.Column.Name}»: {exception.Message}";
            }
        }
        if (firstError is null)
        {
            try
            {
                new DatabaseInsertRequest(_capabilities.Table, _capabilities.ConfirmationHandle!.Value, values).Validate();
            }
            catch (ArgumentException exception)
            {
                firstError = exception.Message;
            }
        }
        return values;
    }

    private void Submit()
    {
        var values = CollectValues(out var error);
        if (error is not null)
        {
            _error.Text = error;
            _save.IsEnabled = false;
            return;
        }
        Close(new DatabaseInsertChoice(values));
    }

    private static string FieldHint(InsertableDatabaseColumn column)
    {
        if (column.MustUseDefault)
            return column.ReadOnlyReason ?? column.DefaultDescription ?? "Поле всегда заполняет сервер.";
        if (!column.Column.CanEdit)
            return column.ReadOnlyReason ?? column.DefaultDescription ?? "Поле доступно только для заполнения сервером.";
        var choices = new List<string> { column.Column.IsNullable ? "доступны NULL и точное значение" : "требуется точное значение" };
        if (column.CanUseDefault)
            choices.Add(column.DefaultDescription is { Length: > 0 }
                ? $"сервер: {column.DefaultDescription}"
                : "можно заполнить сервером");
        if (column.ReadOnlyReason is { Length: > 0 })
            choices.Add(column.ReadOnlyReason);
        return $"Тип СУБД: {column.Column.NativeType} · {string.Join(" · ", choices)}.";
    }

    private static string Preview(DatabaseInsertValue input)
    {
        var value = input.Mode switch
        {
            DatabaseInsertValueMode.ServerDefault => "заполнит сервер",
            DatabaseInsertValueMode.Null => "NULL",
            _ when input.Value?.Length == 0 => "пустая строка «»",
            _ => Visible(input.Value ?? string.Empty)
        };
        return $"{input.ColumnName}: {value}";
    }

    private static string Visible(string value)
    {
        var visible = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
        if (visible.Length > 120)
        {
            var previewLength = char.IsHighSurrogate(visible[119]) && char.IsLowSurrogate(visible[120]) ? 119 : 120;
            return $"«{visible[..previewLength]}…» (исходная длина: {value.Length})";
        }
        var edgeWhitespace = value.Length > 0
            && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));
        return edgeWhitespace
            ? $"«{visible}» (длина: {value.Length}; краевые пробелы сохранены)"
            : $"«{visible}»";
    }
}
