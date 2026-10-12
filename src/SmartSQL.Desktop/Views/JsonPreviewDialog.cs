using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class JsonPreviewDialog : Window
{
    private sealed record PreviewRow(IReadOnlyList<string> Cells);

    public JsonPreviewDialog(JsonTableImportPreview preview, TableDefinition table)
    {
        Title = "Предварительный просмотр JSON · SmartSQL";
        Width = 800;
        Height = 480;
        MinWidth = 600;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var types = preview.Headers.Select(header => table.Fields.Single(field =>
            string.Equals(field.Name, header, StringComparison.OrdinalIgnoreCase)).Type).ToArray();
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = preview.SampleRows.Select(row => new PreviewRow(row.Select((value, index) =>
                types[index] == FieldType.Boolean ? value switch { "1" => "Да", "0" => "Нет", _ => value } : value).ToArray())).ToArray()
        };
        for (var index = 0; index < preview.Headers.Count; index++)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = preview.Headers[index],
                Binding = new Binding($"Cells[{index}]") { Mode = BindingMode.OneWay },
                Width = new DataGridLength(160)
            });

        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(false);
        var import = new Button { Content = $"Импортировать {preview.RowCount}", IsEnabled = preview.RowCount > 0 };
        import.Click += (_, _) => Close(true);
        var layout = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12
        };
        layout.Children.Add(new TextBlock { Text = "Проверка перед импортом", FontSize = 24, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        var description = new TextBlock
        {
            Text = $"Найдено записей: {preview.RowCount}. Показаны первые {preview.SampleRows.Count}. Поля и типы совпадают с выбранной таблицей. После подтверждения записи будут добавлены.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);
        Grid.SetRow(grid, 2);
        layout.Children.Add(grid);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, import }
        };
        Grid.SetRow(actions, 3);
        layout.Children.Add(actions);
        Content = layout;
    }
}
