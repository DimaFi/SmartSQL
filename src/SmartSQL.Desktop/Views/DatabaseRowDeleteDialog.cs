using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

/// <summary>Confirms deletion of exactly one row already identified by a provider-private handle.</summary>
public sealed class DatabaseRowDeleteDialog : Window
{
    private const int MaxKeyValuePreview = 320;
    private const int MaxRowValuePreview = 200;
    private const int MaxRowPreview = 4_000;
    private readonly CheckBox _confirmation = new();
    private readonly Button _delete = new()
    {
        Content = "Удалить одну выбранную строку", IsEnabled = false, Classes = { "danger" }
    };
    private bool _submitted;

    public DatabaseRowDeleteDialog(string databaseName, DatabaseTableRef table,
        IReadOnlyList<DatabaseColumn> columns, IReadOnlyList<string?> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);
        if (columns.Count != values.Count)
            throw new ArgumentException("Строка не соответствует текущей структуре таблицы. Обновите страницу.", nameof(values));
        var keys = columns.Select((column, index) => (Column: column, Index: index))
            .Where(item => item.Column.PrimaryKeyOrder is not null)
            .OrderBy(item => item.Column.PrimaryKeyOrder)
            .ToArray();
        if (keys.Length == 0)
            throw new ArgumentException("У выбранной строки нет подтверждённого первичного ключа. Обновите страницу.", nameof(columns));

        Classes.Add("dialog");
        Title = "Удалить выбранную строку · SmartSQL";
        Width = 700;
        Height = 720;
        MinWidth = 520;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var identity = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                IdentityLine("База", databaseName),
                IdentityLine("Схема", table.Schema),
                IdentityLine("Таблица", table.Name)
            }
        };
        var keyPreview = string.Join("\n", keys.Select(item =>
            $"{Escape(item.Column.Name)} = {Preview(values[item.Index], MaxKeyValuePreview)}"));
        var rowPreview = BuildRowPreview(columns, values);
        _confirmation.Content = new TextBlock
        {
            Text = "Я проверил(а) базу, таблицу и выбранную строку и подтверждаю безвозвратное удаление.",
            TextWrapping = TextWrapping.Wrap
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Строка будет удалена безвозвратно",
            Classes = { "error" },
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "SmartSQL удалит только выбранную строку. Операция не будет повторена автоматически.",
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new Border
        {
            Classes = { "card" },
            Child = identity
        });
        content.Children.Add(new TextBlock { Text = "Первичный ключ выбранной строки", Classes = { "sectionTitle" } });
        content.Children.Add(PreviewBox(keyPreview, 120));
        content.Children.Add(new TextBlock { Text = "Предпросмотр выбранной строки", Classes = { "sectionTitle" } });
        content.Children.Add(PreviewBox(rowPreview, 220));
        content.Children.Add(new TextBlock
        {
            Text = "Длинные значения сокращены только в предпросмотре. Перед подтверждением проверьте первичный ключ и данные строки.",
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(_confirmation);

        _confirmation.IsCheckedChanged += (_, _) =>
            _delete.IsEnabled = !_submitted && _confirmation.IsChecked == true;
        _delete.Click += (_, _) => Submit();
        var cancel = new Button { Content = "Отмена", Classes = { "quiet" } };
        cancel.Click += (_, _) => Close(false);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, _delete }
        };
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var layout = new Grid
        {
            Margin = new Thickness(22),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14
        };
        layout.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Подтверждение удаления", Classes = { "pageTitle" } },
                new TextBlock
                {
                    Text = "Сверьте расположение, первичный ключ и значения перед удалением.",
                    Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
                }
            }
        });
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        Content = layout;
    }

    private void Submit()
    {
        if (_submitted || _confirmation.IsChecked != true)
            return;
        _submitted = true;
        _delete.IsEnabled = false;
        Close(true);
    }

    private static Grid IdentityLine(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), ColumnSpacing = 10 };
        row.Children.Add(new TextBlock { Text = label, Classes = { "caption" } });
        var text = new TextBlock
        {
            Text = value, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static TextBox PreviewBox(string text, double maxHeight) => new()
    {
        Text = text,
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 64,
        MaxHeight = maxHeight
    };

    private static string BuildRowPreview(IReadOnlyList<DatabaseColumn> columns, IReadOnlyList<string?> values)
    {
        var result = new StringBuilder();
        for (var index = 0; index < columns.Count; index++)
        {
            var line = $"{Escape(columns[index].Name)} = {Preview(values[index], MaxRowValuePreview)}";
            var separatorLength = result.Length == 0 ? 0 : Environment.NewLine.Length;
            if (result.Length + separatorLength + line.Length > MaxRowPreview)
            {
                var omitted = columns.Count - index;
                if (result.Length > 0)
                    result.AppendLine();
                result.Append($"… ещё полей: {omitted}");
                break;
            }
            if (result.Length > 0)
                result.AppendLine();
            result.Append(line);
        }
        return result.ToString();
    }

    private static string Preview(string? value, int maximum)
    {
        if (value is null)
            return "NULL";
        if (value.Length == 0)
            return "пустая строка «»";
        var visible = EscapeBounded(value, maximum, out var truncated);
        if (!truncated)
            return $"«{visible}»";
        return $"«{visible}…» (исходная длина: {value.Length})";
    }

    private static string EscapeBounded(string value, int maximum, out bool truncated)
    {
        var result = new StringBuilder(Math.Min(value.Length, maximum));
        var index = 0;
        while (index < value.Length)
        {
            string? escaped = value[index] switch
            {
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => null
            };
            if (escaped is not null)
            {
                if (result.Length + escaped.Length > maximum)
                    break;
                result.Append(escaped);
                index++;
                continue;
            }
            var characterLength = char.IsHighSurrogate(value[index]) && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]) ? 2 : 1;
            if (result.Length + characterLength > maximum)
                break;
            result.Append(value, index, characterLength);
            index += characterLength;
        }
        truncated = index < value.Length;
        return result.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);
}
