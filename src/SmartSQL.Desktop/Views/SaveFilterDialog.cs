using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SmartSQL.Desktop.Views;

public sealed class SaveFilterDialog : Window
{
    public SaveFilterDialog(string? suggestedName = null)
    {
        Title = "Сохранить фильтр · SmartSQL";
        Width = 420;
        Height = 230;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var name = new TextBox { Text = suggestedName ?? string.Empty, PlaceholderText = "Например, Нет документов" };
        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var save = new Button { Content = "Сохранить" };
        save.Click += (_, _) =>
        {
            var value = name.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                error.Text = "Укажите название фильтра.";
                return;
            }
            Close(value);
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Название фильтра", FontSize = 23, FontWeight = FontWeight.SemiBold },
                name,
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, save }
                }
            }
        };
    }
}
