using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public sealed class AddFieldDialog : Window
{
    private static readonly FieldType[] Types = Enum.GetValues<FieldType>();

    public AddFieldDialog(IDataWorkspace workspace, Guid tableId)
    {
        Title = "Добавить поле · SmartSQL";
        Width = 400;
        Height = 290;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var name = new TextBox { PlaceholderText = "Название нового поля" };
        var type = new ComboBox
        {
            ItemsSource = new[] { "Текст", "Число", "Дата", "Да / Нет" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
        var cancel = new Button { Content = "Отмена" };
        cancel.Click += (_, _) => Close(null);
        var create = new Button { Content = "Добавить" };
        create.Click += (_, _) =>
        {
            try
            {
                if (type.SelectedIndex < 0)
                    throw new ArgumentException("Выберите тип поля.");
                var field = workspace.AddField(tableId, new FieldInput(name.Text ?? string.Empty, Types[type.SelectedIndex]));
                Close(field);
            }
            catch (Exception exception)
            {
                error.Text = exception.Message;
            }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Новое поле", FontSize = 23, FontWeight = FontWeight.SemiBold },
                name,
                type,
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, create }
                }
            }
        };
    }
}
