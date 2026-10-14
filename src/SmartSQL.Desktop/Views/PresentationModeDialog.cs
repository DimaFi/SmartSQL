using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

/// <summary>Returns a nullable presentation preference through ShowDialog; closing leaves it unset.</summary>
public sealed class PresentationModeDialog : Window
{
    public PresentationModeDialog()
    {
        Title = "Выбор режима · SmartSQL";
        Width = 580;
        Height = 390;
        MinWidth = 440;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var simple = new Button { Content = "Простой режим", HorizontalAlignment = HorizontalAlignment.Stretch };
        simple.Click += (_, _) => Close((WorkspacePresentationMode?)WorkspacePresentationMode.Simple);
        var advanced = new Button { Content = "Расширенный режим", HorizontalAlignment = HorizontalAlignment.Stretch };
        advanced.Click += (_, _) => Close((WorkspacePresentationMode?)WorkspacePresentationMode.Advanced);
        var cancel = new Button { Content = "Выбрать позже", HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => Close(null);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Как вам удобнее работать?", FontSize = 23, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Режим можно сменить в главном окне. Выбор запоминается на этом компьютере.", TextWrapping = TextWrapping.Wrap },
                    simple,
                    new TextBlock { Text = "Основные действия с таблицами и данными, меньше технических подробностей.", TextWrapping = TextWrapping.Wrap },
                    advanced,
                    new TextBlock { Text = "Больше сведений о подключениях и структуре базы данных.", TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Оба режима сохраняют ваши данные. Режим не меняет права доступа и не разрешает запись в базу.", TextWrapping = TextWrapping.Wrap },
                    cancel
                }
            }
        };
    }
}
