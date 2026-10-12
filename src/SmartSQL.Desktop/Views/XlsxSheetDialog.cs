using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Infrastructure;

namespace SmartSQL.Desktop.Views;

public sealed class XlsxSheetDialog : Window
{
    private readonly ComboBox _sheets;
    private readonly TextBlock _warnings = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _confirm = new() { Content = "Понимаю перечисленные особенности переноса", IsVisible = false };
    private readonly Button _continue = new() { Content = "Сопоставить столбцы", IsEnabled = false };
    private int _revision;

    public XlsxSheetDialog(XlsxWorkbookSource workbook)
    {
        Title = "Выбор листа XLSX · SmartSQL";
        Width = 720;
        Height = 510;
        MinWidth = 540;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _sheets = new ComboBox { ItemsSource = workbook.Worksheets, HorizontalAlignment = HorizontalAlignment.Stretch };
        _sheets.SelectionChanged += async (_, _) =>
        {
            var revision = ++_revision;
            _continue.IsEnabled = false;
            _confirm.IsChecked = false;
            _confirm.IsVisible = false;
            _warnings.Foreground = Brushes.DarkOrange;
            _warnings.Text = "Проверка листа…";
            if (_sheets.SelectedItem is not string name) return;
            try
            {
                var warnings = await Task.Run(() => workbook.GetWarnings(name));
                if (revision != _revision) return;
                _warnings.Text = warnings.Count == 0 ? "Данные листа совместимы." : string.Join("\n\n", warnings);
                _confirm.IsVisible = warnings.Count > 0;
                _continue.IsEnabled = warnings.Count == 0;
            }
            catch (Exception exception)
            {
                if (revision != _revision) return;
                _warnings.Foreground = Brushes.Firebrick;
                _warnings.Text = exception.Message;
            }
        };
        _confirm.IsCheckedChanged += (_, _) => _continue.IsEnabled = _confirm.IsVisible && _confirm.IsChecked == true;
        _continue.Click += (_, _) => Close(_sheets.SelectedItem as string);
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), RowSpacing = 12 };
        layout.Children.Add(new TextBlock { Text = "Выберите лист книги", FontSize = 24, FontWeight = FontWeight.SemiBold });
        Add(layout, new TextBlock { Text = "Первая строка — заголовки. Будет добавлен только выбранный лист; остальные листы сохраняются в исходном файле. Исходная книга не изменяется.", TextWrapping = TextWrapping.Wrap }, 1);
        Add(layout, _sheets, 2);
        Add(layout, new ScrollViewer { Content = _warnings }, 3);
        Add(layout, _confirm, 4);
        Add(layout, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _continue } }, 5);
        Content = layout;
        _sheets.SelectedIndex = 0;
    }

    private static void Add(Grid grid, Control child, int row) { Grid.SetRow(child, row); grid.Children.Add(child); }
}
