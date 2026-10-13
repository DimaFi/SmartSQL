using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using SmartSQL.Desktop.Presentation;

namespace SmartSQL.Desktop.Views;

/// <summary>Shows the complete provider display value without trimming or type conversion.</summary>
public sealed class CellValueDialog : Window
{
    public CellValueDialog(string fieldName, string? value)
    {
        Title = $"Значение поля · {fieldName}";
        Width = 760;
        Height = 520;
        MinWidth = 420;
        MinHeight = 280;
        Classes.Add("dialog");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var status = new TextBlock
        {
            Text = value is null ? "NULL — отсутствующее значение"
                : value.Length == 0 ? "Пустая строка · 0 символов" : $"Полное значение · {value.Length:N0} символов",
            Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
        };
        var copy = new Button { Content = IconLabel("UiIconCopy", "Копировать значение"), IsEnabled = value is not null, Classes = { "primary" } };
        copy.Click += async (_, _) =>
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is null || value is null)
                return;
            try
            {
                await clipboard.SetTextAsync(value);
                status.Text = "Полное значение скопировано.";
            }
            catch
            {
                status.Text = "Не удалось скопировать значение в буфер обмена.";
            }
        };
        var close = new Button { Content = "Закрыть", Classes = { "quiet" } };
        close.Click += (_, _) => Close();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            Close();
        };
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), RowSpacing = 12 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), ColumnSpacing = 12 };
        var icon = UiIcon.Create("UiIconText", 25);
        var iconBox = new Border { Child = icon, Padding = new Thickness(10), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
        iconBox.Bind(Border.BackgroundProperty, new DynamicResourceExtension("UiAccentSoftBrush"));
        icon.Bind(PathIcon.ForegroundProperty, new DynamicResourceExtension("UiAccentBrush"));
        header.Children.Add(iconBox);
        var title = new StackPanel { Spacing = 3, Children = {
            new TextBlock { Text = "ЗНАЧЕНИЕ ПОЛЯ", Classes = { "caption" }, FontSize = 11 },
            new TextBlock { Text = fieldName, FontSize = 21, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }
        } };
        ToolTip.SetTip(title, fieldName);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        layout.Children.Add(header);
        Grid.SetRow(status, 1);
        layout.Children.Add(status);
        var content = new TextBox
        {
            Text = value ?? string.Empty, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 14,
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        content.Bind(TextBox.BackgroundProperty, new DynamicResourceExtension("UiPaperBrush"));
        Grid.SetRow(content, 2);
        layout.Children.Add(content);
        var note = new TextBlock { Text = "Полное текстовое представление PostgreSQL · без обрезки и изменения точности", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(note, 3);
        layout.Children.Add(note);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { close, copy } };
        Grid.SetRow(actions, 4);
        layout.Children.Add(actions);
        Content = layout;
    }

    private static StackPanel IconLabel(string icon, string text) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 8,
        Children = { UiIcon.Create(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } }
    };
}
