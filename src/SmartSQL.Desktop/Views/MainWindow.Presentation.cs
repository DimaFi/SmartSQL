using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow
{
    private bool _presentationInitialized;

    private void InitializePresentation()
    {
        _presentationInitialized = true;
        SizeChanged += OnPresentationSizeChanged;
        ApplyPresentationMode();
        RefreshPresentation();
    }

    private void OnPresentationModeChanged(object? sender, RoutedEventArgs e)
    {
        if (!_presentationInitialized)
            return;

        ApplyPresentationMode();
    }

    private void ApplyPresentationMode()
    {
        AdvancedToolsPanel.IsVisible = PresentationModeToggle.IsChecked == true;
        UpdateResponsiveLayout();
    }

    private void OnPresentationSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout();

    private void UpdateResponsiveLayout()
    {
        if (!_presentationInitialized)
            return;

        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        var height = Bounds.Height > 0 ? Bounds.Height : Height;
        var compact = width < 1180;
        var advanced = PresentationModeToggle.IsChecked == true;

        BrandTaglineText.IsVisible = !compact;
        QuickPasteRowsButton.IsVisible = !compact;
        AddButtonCaption.Text = compact ? "Добавить" : "Новая запись";
        HeaderActionsPanel.MaxWidth = compact ? 180 : 420;
        AdvancedToolsPanel.Width = compact ? 240 : 292;
        BottomHintPanel.IsVisible = !advanced && height >= 700;
    }

    private void OnShowStructure(object? sender, RoutedEventArgs e)
    {
        PresentationModeToggle.IsChecked = true;
        AdvancedTabs.SelectedIndex = 1;
        ApplyPresentationMode();
    }

    private void SetFilterCaption(int count) =>
        FilterCaptionText.Text = count == 0 ? "Фильтр" : $"Фильтры: {count}";

    private void RefreshPresentation()
    {
        if (!_presentationInitialized)
            return;

        HeaderDatabasePathText.Text = _workspace?.FilePath ?? "База не открыта";

        var tableCount = _workspace?.GetTables().Count ?? 0;
        TableCountText.Text = tableCount == 0
            ? "Нет таблиц"
            : $"{tableCount} {Decline(tableCount, "таблица", "таблицы", "таблиц")}";

        var hasTable = _currentTable is not null;
        QuickImportXlsxButton.IsEnabled = hasTable;
        QuickPasteRowsButton.IsEnabled = hasTable;
        HintFilterButton.IsEnabled = hasTable;
        HintStructureButton.IsEnabled = hasTable;
        HintPasteButton.IsEnabled = hasTable;

        if (_currentTable is null)
        {
            StructureTableNameText.Text = "Таблица не выбрана";
            StructureSummaryText.Text = "Выберите таблицу слева";
            StructureFieldsList.ItemsSource = null;
            return;
        }

        StructureTableNameText.Text = _currentTable.Name;
        var recordCount = _workspace?.CountRecords(_currentTable.Id) ?? 0;
        var fieldCount = _currentTable.Fields.Count;
        StructureSummaryText.Text =
            $"{fieldCount} {Decline(fieldCount, "поле", "поля", "полей")} · " +
            $"{recordCount} {Decline(recordCount, "запись", "записи", "записей")}";
        StructureFieldsList.ItemsSource = _currentTable.Fields
            .OrderBy(field => field.Position)
            .Select(field => $"{field.Position + 1}. {field.Name} · {TypeLabel(field.Type)}")
            .ToArray();
    }

    private static string Decline(int number, string singular, string few, string many)
    {
        var absolute = Math.Abs(number) % 100;
        if (absolute is >= 11 and <= 19)
            return many;

        return (absolute % 10) switch
        {
            1 => singular,
            2 or 3 or 4 => few,
            _ => many
        };
    }
}
