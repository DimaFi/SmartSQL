using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Desktop.Presentation;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow
{
    private sealed record ColumnHeaderState(Guid FieldId, PathIcon Direction);

    private Button CreateFieldHeader(FieldDefinition field, int index)
    {
        var iconKey = field.Type switch
        {
            FieldType.Number => "UiIconNumber",
            FieldType.Boolean => "UiIconBoolean",
            FieldType.Date => "UiIconCalendar",
            _ => "UiIconText"
        };
        var direction = UiIcon.Create("UiIconChevronDown", 10);
        direction.IsVisible = false;
        var label = new TextBlock { Text = field.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        content.Children.Add(UiIcon.Create(iconKey, 15));
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        Grid.SetColumn(direction, 2);
        content.Children.Add(direction);
        var header = new Button
        {
            Classes = { "columnHeader" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0), MinHeight = 0, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Tag = new ColumnHeaderState(field.Id, direction),
            Content = content
        };
        ToolTip.SetTip(header, $"{field.Name} · {TypeLabel(field.Type)}. Нажмите, чтобы отсортировать всю таблицу.");
        header.Click += (_, _) =>
        {
            if (_currentTable is null || _transferInProgress) return;
            var descending = SortFieldBox.SelectedIndex == index + 1 && DescendingBox.IsChecked != true;
            _updatingSort = true;
            SortFieldBox.SelectedIndex = index + 1;
            DescendingBox.IsChecked = descending;
            _updatingSort = false;
            OnSortChanged(header, new RoutedEventArgs());
        };
        return header;
    }

    private void RefreshColumnHeaders()
    {
        foreach (var column in RecordsGrid.Columns)
        {
            if (column.Header is not Button { Tag: ColumnHeaderState state }) continue;
            state.Direction.IsVisible = state.FieldId == _sortFieldId;
            state.Direction.RenderTransform = new RotateTransform(_sortDescending ? 0 : 180);
        }
    }
}
