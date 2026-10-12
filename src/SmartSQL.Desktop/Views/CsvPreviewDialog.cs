using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

public sealed class CsvPreviewDialog : Window
{
    private sealed record PreviewRow(IReadOnlyList<string> Cells);

    public CsvPreviewDialog(CsvImportPreview preview)
    {
        Title = "Предварительный просмотр CSV · SmartSQL";
        Width = 800;
        Height = 480;
        MinWidth = 600;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = preview.SampleRows.Select(row => new PreviewRow(row)).ToArray()
        };
        for (var index = 0; index < preview.Headers.Count; index++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = $"{preview.Headers[index]} → {preview.TargetFields[index] ?? "пропуск"}",
                Binding = new Binding($"Cells[{index}]") { Mode = BindingMode.OneWay },
                Width = new DataGridLength(160)
            });
        }
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
        layout.Children.Add(new TextBlock { Text = "Проверка перед импортом", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var description = new TextBlock
        {
            Text = $"Найдено записей: {preview.RowCount}. Показаны первые {preview.SampleRows.Count}. После подтверждения они будут добавлены в выбранную таблицу."
                + (preview.SkippedColumns.Count > 0 ? $" Не импортируются: {string.Join(", ", preview.SkippedColumns)}." : "")
                + (preview.EmptyFields.Count > 0 ? $" Останутся пустыми: {string.Join(", ", preview.EmptyFields)}." : ""),
            TextWrapping = TextWrapping.Wrap
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
