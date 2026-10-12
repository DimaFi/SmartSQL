using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

public sealed class ClipboardPreviewDialog : Window
{
    private sealed record PreviewRow(IReadOnlyList<string> Cells);

    public ClipboardPreviewDialog(ClipboardImportPreview preview)
    {
        Title = "Предварительный просмотр вставки · SmartSQL";
        Width = 850;
        Height = 500;
        MinWidth = 600;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var data = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true,
            ItemsSource = preview.SampleRows.Select(row => new PreviewRow(row)).ToArray()
        };
        for (var index = 0; index < preview.ColumnCount; index++)
            data.Columns.Add(new DataGridTextColumn
            {
                Header = $"{preview.Headers[index]} → {preview.TargetFields[index] ?? "пропуск"}",
                Binding = new Binding($"Cells[{index}]") { Mode = BindingMode.OneWay },
                Width = new DataGridLength(170)
            });
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(false);
        var insert = new Button { Content = $"Добавить {preview.RowCount:N0} записей", IsEnabled = preview.RowCount > 0 };
        insert.Click += (_, _) => Close(true);
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
        layout.Children.Add(new TextBlock { Text = "Проверка перед вставкой", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var description = new TextBlock
        {
            Text = $"Строк данных: {preview.RowCount:N0} · столбцов: {preview.ColumnCount}. Показаны первые {preview.SampleRows.Count} строк. Все значения проверены. Записи будут добавлены в выбранную таблицу; пустые ячейки сохраняются как пустые значения."
                + (preview.SkippedColumns.Count > 0 ? $" Не вставляются: {string.Join(", ", preview.SkippedColumns)}." : "")
                + (preview.EmptyFields.Count > 0 ? $" Останутся пустыми: {string.Join(", ", preview.EmptyFields)}." : ""),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);
        Grid.SetRow(data, 2);
        layout.Children.Add(data);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, insert }
        };
        Grid.SetRow(actions, 3);
        layout.Children.Add(actions);
        Content = layout;
    }
}
