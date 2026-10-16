using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

/// <summary>Object-owned native metadata only; opaque definitions cannot be executed here.</summary>
public sealed class DatabaseStructureDialog : Window
{
    private sealed record DetailItem(string Summary, Func<string> Details);

    public DatabaseStructureDialog(DatabaseStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        DatabaseStructureLimits.Validate(structure);
        Title = "Структура объекта · SmartSQL";
        Width = 940;
        Height = 560;
        MinWidth = 600;
        MinHeight = 360;
        Classes.Add("dialog");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var identity = $"{structure.Table.Schema}.{structure.Table.Name}";
        var heading = new TextBlock
        {
            Text = identity, FontSize = 21, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTip.SetTip(heading, identity);
        var scope = new TextBlock
        {
            Text = "Только ограничения и индексы выбранного объекта. Входящие связи и ограничения доменов не входят в этот охват. " +
                "Метаданные прочитаны отдельным снимком и могут отличаться от загруженной страницы данных.",
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
        };
        var coverage = new TextBlock
        {
            Text = $"Ограничения: {CoverageLabel(structure.ConstraintCoverage)} · Индексы: {CoverageLabel(structure.IndexCoverage)}. Диагностика — на вкладке «Определение».",
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
        };
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = $"Поля ({structure.Columns.Count})", Content = FieldDetails(structure.Columns) },
                new TabItem { Header = $"Ограничения ({structure.Constraints.Count})", Content = ConstraintDetails(structure) },
                new TabItem { Header = $"Индексы ({structure.Indexes.Count})", Content = IndexDetails(structure) },
                new TabItem { Header = "Определение", Content = DefinitionDetails(structure) }
            },
            SelectedIndex = 0
        };
        AutomationProperties.SetName(tabs, "Подробная структура объекта только для чтения");
        var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            Close();
        };
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), RowSpacing = 8 };
        Add(layout, heading, 0);
        Add(layout, scope, 1);
        Add(layout, coverage, 2);
        Add(layout, tabs, 3);
        Add(layout, close, 4);
        Content = layout;
    }

    private static Control FieldDetails(IReadOnlyList<DatabaseColumn> columns) => ListDetails(columns
        .OrderBy(column => column.Ordinal)
        .Select(column => new DetailItem($"{column.Ordinal + 1}. {column.Name}", () =>
            $"Позиция: {column.Ordinal + 1}\nИмя поля: {column.Name}\nТип СУБД: {Text(column.NativeType)}\n" +
            $"Допускает NULL: {Flag(column.IsNullable)}\nПорядок в первичном ключе: {(column.PrimaryKeyOrder is { } order ? order.ToString() : "Не входит в PK")}\n\n" +
            "Это сведения о структуре, а не разрешение на запись.")),
        "Поля объекта", "Столбцы не предоставлены провайдером.");

    private static Control ConstraintDetails(DatabaseStructure structure) => Section(
        $"Охват ограничений: {CoverageLabel(structure.ConstraintCoverage)}. Отдельные неизвестные флаги показаны как «Неизвестно».",
        ListDetails(structure.Constraints.Select((constraint, index) => new DetailItem(
            $"{index + 1}. {ConstraintLabel(constraint.Kind)} · {constraint.Name ?? "без предоставленного имени"}",
            () => DescribeConstraint(constraint))), "Ограничения выбранного объекта",
            EmptyLabel(structure.ConstraintCoverage, "ограничения")));

    private static string DescribeConstraint(DatabaseConstraint constraint)
    {
        var result = new StringBuilder();
        result.AppendLine($"Имя: {Text(constraint.Name)}");
        result.AppendLine($"Вид: {ConstraintLabel(constraint.Kind)}");
        result.AppendLine($"Нативный вид: {Text(constraint.NativeKind)}");
        result.AppendLine($"Может быть отложено: {Flag(constraint.IsDeferrable)}");
        result.AppendLine($"Изначально отложено: {Flag(constraint.IsInitiallyDeferred)}");
        result.AppendLine($"Проверено СУБД (validated): {Flag(constraint.IsValidated)}");
        result.AppendLine();
        result.AppendLine("Поля в нативном порядке:");
        if (constraint.Columns.Count == 0) result.AppendLine("Не предоставлены / неприменимо для этого ограничения.");
        for (var index = 0; index < constraint.Columns.Count; index++)
            result.AppendLine($"{index + 1}. {constraint.Columns[index]}");
        if (constraint.ForeignKey is { } foreign)
        {
            result.AppendLine();
            result.AppendLine($"Схема назначения: {Text(foreign.ReferencedSchema)}");
            result.AppendLine($"Таблица назначения: {foreign.ReferencedTable}");
            result.AppendLine("Сопоставление полей по позиции:");
            var count = Math.Max(constraint.Columns.Count, foreign.ReferencedColumns.Count);
            if (count == 0) result.AppendLine("Сопоставление провайдером не предоставлено.");
            for (var index = 0; index < count; index++)
            {
                var source = index < constraint.Columns.Count ? constraint.Columns[index] : null;
                var referenced = index < foreign.ReferencedColumns.Count ? foreign.ReferencedColumns[index] : null;
                result.AppendLine($"{index + 1}. {Text(source)} → {Text(referenced)}");
            }
            result.AppendLine($"ON UPDATE: {Text(foreign.OnUpdate)}");
            result.AppendLine($"ON DELETE: {Text(foreign.OnDelete)}");
            result.AppendLine($"MATCH: {Text(foreign.Match)}");
        }
        result.AppendLine();
        result.AppendLine("Нативное определение (только просмотр):");
        result.Append(constraint.NativeDefinition ?? "Провайдер не предоставил определение.");
        return result.ToString();
    }

    private static Control IndexDetails(DatabaseStructure structure) => Section(
        $"Охват индексов: {CoverageLabel(structure.IndexCoverage)}. Ключевые элементы отделены от INCLUDE / вспомогательных элементов.",
        ListDetails(structure.Indexes.Select((index, position) => new DetailItem($"{position + 1}. {index.Name}",
            () => DescribeIndex(index))), "Индексы выбранного объекта", EmptyLabel(structure.IndexCoverage, "индексы")));

    private static string DescribeIndex(DatabaseIndex index)
    {
        var result = new StringBuilder();
        result.AppendLine($"Имя: {index.Name}");
        result.AppendLine($"Метод СУБД: {Text(index.NativeMethod)}");
        result.AppendLine($"Уникальный: {Flag(index.IsUnique)}");
        result.AppendLine($"Первичный: {Flag(index.IsPrimary)}");
        result.AppendLine($"Частичный: {Flag(index.IsPartial)}");
        result.AppendLine($"Действителен (valid): {Flag(index.IsValid)}");
        result.AppendLine($"Готов к использованию (ready): {Flag(index.IsReady)}");
        result.AppendLine();
        result.AppendLine("Элементы в нативном порядке:");
        if (index.Terms.Count == 0) result.AppendLine("Не предоставлены провайдером.");
        foreach (var term in index.Terms.OrderBy(term => term.Ordinal))
        {
            result.AppendLine($"{term.Ordinal}. {(term.IsKey ? "Ключевой элемент" : "INCLUDE / вспомогательный элемент")} · {TermLabel(term.Kind)}");
            result.AppendLine($"   Поле: {Text(term.ColumnName)}");
            result.AppendLine($"   По убыванию: {Flag(term.Descending)}");
            result.AppendLine($"   Collation: {Text(term.Collation)}");
            result.AppendLine("   Нативное определение элемента:");
            result.AppendLine(term.NativeDefinition ?? "   Не предоставлено провайдером.");
            result.AppendLine();
        }
        result.AppendLine("Нативное определение индекса (только просмотр):");
        result.Append(index.NativeDefinition ?? "Провайдер не предоставил определение.");
        return result.ToString();
    }

    private static Control DefinitionDetails(DatabaseStructure structure)
    {
        var diagnostics = new StringBuilder();
        diagnostics.AppendLine($"Ограничения: {CoverageLabel(structure.ConstraintCoverage)}");
        diagnostics.AppendLine($"Индексы: {CoverageLabel(structure.IndexCoverage)}");
        diagnostics.AppendLine("Охват — только выбранный объект; метаданные получены отдельным чтением.");
        diagnostics.AppendLine();
        if (structure.Diagnostics.Count == 0) diagnostics.AppendLine("Дополнительная диагностика провайдером не предоставлена.");
        foreach (var diagnostic in structure.Diagnostics)
        {
            diagnostics.AppendLine($"[{diagnostic.Code}]");
            diagnostics.AppendLine(diagnostic.Message);
            diagnostics.AppendLine();
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*"), RowSpacing = 8 };
        Add(layout, new TextBlock { Text = "Нативное определение объекта · только просмотр, без выполнения", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, 0);
        Add(layout, ReadOnlyText(structure.NativeDefinition ?? "Провайдер не предоставил полное определение объекта.", "Нативное определение объекта"), 1);
        Add(layout, new TextBlock { Text = "Охват и диагностика провайдера", Classes = { "sectionTitle" } }, 2);
        Add(layout, ReadOnlyText(diagnostics.ToString(), "Охват и диагностика структуры"), 3);
        return layout;
    }

    private static Control ListDetails(IEnumerable<DetailItem> source, string accessibleName, string empty)
    {
        var items = source.ToArray();
        var details = ReadOnlyText(empty, accessibleName + ": полные сведения");
        var list = new ListBox
        {
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<DetailItem>((item, _) => new TextBlock
            {
                Text = item?.Summary, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 5), FontSize = 13
            })
        };
        AutomationProperties.SetName(list, accessibleName);
        list.SelectionChanged += (_, _) => details.Text = list.SelectedItem is DetailItem item ? item.Details() : empty;
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("220,5,*"), ColumnSpacing = 6 };
        layout.Children.Add(list);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        layout.Children.Add(splitter);
        Grid.SetColumn(details, 2);
        layout.Children.Add(details);
        if (items.Length > 0) list.SelectedIndex = 0;
        return layout;
    }

    private static TextBox ReadOnlyText(string text, string accessibleName)
    {
        var content = new TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(10)
        };
        content.Bind(TextBox.BackgroundProperty, new DynamicResourceExtension("UiPaperBrush"));
        ScrollViewer.SetHorizontalScrollBarVisibility(content, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(content, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(content, accessibleName);
        ToolTip.SetTip(content, "Полные сведения без обрезки. Текст можно выделить и скопировать (Ctrl+A, Ctrl+C).");
        return content;
    }

    private static Control Section(string text, Control content)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        Add(layout, new TextBlock { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, 0);
        Add(layout, content, 1);
        return layout;
    }

    private static void Add(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private static string Flag(bool? value) => value is null ? "Неизвестно" : value.Value ? "Да" : "Нет";
    private static string Text(string? value) => value is null ? "Не предоставлено / неизвестно" : value.Length == 0 ? "⟨пусто⟩" : value;
    private static string CoverageLabel(DatabaseMetadataCoverage coverage) => coverage switch
    {
        DatabaseMetadataCoverage.Complete => "полные в указанном охвате",
        DatabaseMetadataCoverage.Partial => "частичные",
        DatabaseMetadataCoverage.Unavailable => "недоступны",
        _ => "охват не подтверждён"
    };
    private static string EmptyLabel(DatabaseMetadataCoverage coverage, string objects) => coverage == DatabaseMetadataCoverage.Complete
        ? $"В подтверждённом охвате {objects} отсутствуют."
        : $"Сведения не предоставлены. Это не подтверждает, что {objects} отсутствуют; см. охват и диагностику.";
    private static string ConstraintLabel(DatabaseConstraintKind kind) => kind switch
    {
        DatabaseConstraintKind.PrimaryKey => "Первичный ключ",
        DatabaseConstraintKind.Unique => "Уникальность",
        DatabaseConstraintKind.ForeignKey => "Внешний ключ",
        DatabaseConstraintKind.Check => "CHECK",
        DatabaseConstraintKind.Exclusion => "Исключение",
        DatabaseConstraintKind.NotNull => "NOT NULL",
        _ => "Иное ограничение"
    };
    private static string TermLabel(DatabaseIndexTermKind kind) => kind switch
    {
        DatabaseIndexTermKind.Column => "поле",
        DatabaseIndexTermKind.Expression => "выражение",
        DatabaseIndexTermKind.RowIdentifier => "нативный идентификатор строки",
        _ => "иной элемент"
    };
}
