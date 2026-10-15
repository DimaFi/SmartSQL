using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using SmartSQL.Application;
using SmartSQL.Domain;
using SmartSQL.Desktop.Presentation;

namespace SmartSQL.Desktop.Views;

/// <summary>Provider-neutral database browser with optional, explicitly enabled safe editing operations.</summary>
public sealed class DatabaseTableView : UserControl, IAsyncDisposable
{
    private const int PageSize = 100;
    private readonly string _databaseName;
    private readonly string _providerLabel;
    private readonly DatabaseTableRef _table;
    private Func<CancellationToken, Task<IDatabaseBrowser>>? _connectAsync;
    private readonly Border _inspector = new();
    private readonly CheckBox _showStructure = new() { Content = "Структура", VerticalAlignment = VerticalAlignment.Center };
    private readonly HashSet<Window> _dialogs = [];
    private Task? _initialization;
    private Task? _disposal;
    private TaskCompletionSource? _operationCompletion;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _search = new() { PlaceholderText = "Поиск по всем полям…", MaxLength = 256, Width = 225 };
    private readonly ComboBox _sortColumn = new() { Width = 190 };
    private readonly CheckBox _descending = new() { Content = "Убывание", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _applyQuery = new() { Content = "Применить" };
    private readonly Button _editFilters = new() { Content = "Фильтры: 0" };
    private readonly Button _clearQuery = new() { Content = "Сбросить" };
    private readonly DataGrid _data = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly DataGrid _structure = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBlock _identity = new() { Text = "Выберите таблицу", FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _objectKind = new() { Text = "Схемы, таблицы и представления", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _querySummary = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _diagnostics = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _paging = new() { Text = "По 100 записей на странице", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous = new() { Content = "←", IsEnabled = false, Width = 38, Padding = new Thickness(0) };
    private readonly Button _next = new() { Content = "→", IsEnabled = false, Width = 38, Padding = new Thickness(0) };
    private readonly Button _refreshTable = new() { Content = "Обновить", IsEnabled = false };
    private readonly Button _cancelOperation = new() { Content = "Отмена", IsEnabled = false };
    private readonly Button _editCell = new() { Content = "Изменить значение", IsEnabled = false, IsVisible = false };
    private readonly Button _insertRow = new() { Content = "+ Новая запись", IsEnabled = false, IsVisible = false };
    private readonly Button _deleteRow = new() { Content = "Удалить строку", IsEnabled = false, IsVisible = false };
    private readonly TextBlock _mode = new() { VerticalAlignment = VerticalAlignment.Center };
    private IDatabaseBrowser? _browser;
    private IDatabaseEditor? _editor;
    private IDatabaseRowCreator? _rowCreator;
    private IDatabaseRowDeleter? _rowDeleter;
    private DatabasePage? _page;
    private DatabaseEditCapabilities? _editCapabilities;
    private DatabaseInsertCapabilities? _insertCapabilities;
    private DatabaseDeleteCapabilities? _deleteCapabilities;
    private DatabaseQueryCapabilities? _queryCapabilities;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private bool _closed;
    private bool _reloadRequiredBeforeWrite;
    private bool _deleteDialogOpen;
    private DatabaseBrowseQuery _query = new();

    private sealed record SortChoice(string? Name)
    {
        public override string ToString() => Name ?? "Исходный порядок";
    }

    private sealed record DisplayRow(IReadOnlyList<string?> Values, Guid? EditHandle, Guid? DeleteHandle);
    private sealed record ColumnRow(int Position, string Name, string NativeType, string Nullable,
        string PrimaryKey, string Editability, string Insertability);

    public DatabaseTableView(string connectionName, string providerLabel, string endpoint, string databaseName,
        DatabaseTableRef table, Func<CancellationToken, Task<IDatabaseBrowser>> connectAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
        _databaseName = databaseName;
        _providerLabel = providerLabel;
        _connectAsync = connectAsync ?? throw new ArgumentNullException(nameof(connectAsync));
        _identity.Text = table.Name;
        _previous.Click += async (_, _) => await ChangePageAsync(-PageSize);
        _next.Click += async (_, _) => await ChangePageAsync(PageSize);
        _refreshTable.Click += async (_, _) => await RefreshTableAsync();
        _editCell.Click += async (_, _) => await EditSelectedCellAsync();
        _insertRow.Click += async (_, _) => await InsertRowAsync();
        _deleteRow.Click += async (_, _) => await DeleteSelectedRowAsync();
        _data.SelectionChanged += (_, _) => UpdateControls();
        _applyQuery.Click += async (_, _) => await ApplyQueryAsync();
        _editFilters.Click += async (_, _) => await EditFiltersAsync();
        _clearQuery.Click += async (_, _) => { ResetQuery(); await ApplyQueryAsync(); };
        _search.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter) { args.Handled = true; await ApplyQueryAsync(); }
        };
        _cancelOperation.Click += (_, _) => { _operation?.Cancel(); _status.Text = "Отмена операции…"; };
        _showStructure.IsCheckedChanged += (_, _) => _inspector.IsVisible = _showStructure.IsChecked == true;
        _applyQuery.Classes.Add("primary");
        _editCell.Classes.Add("primary");
        _insertRow.Classes.Add("primary");
        _deleteRow.Classes.Add("danger");
        _applyQuery.Content = IconLabel("UiIconSearch", "Применить");
        _refreshTable.Content = IconLabel("UiIconRefresh", "Обновить");
        _previous.Content = UiIcon.Create("UiIconChevronLeft", 16);
        _next.Content = UiIcon.Create("UiIconChevronRight", 16);
        _diagnostics.Classes.Add("muted");
        _status.Classes.Add("caption");
        _sortColumn.ItemTemplate = new FuncDataTemplate<SortChoice>((choice, _) =>
        {
            var text = new TextBlock { Text = choice?.ToString(), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            ToolTip.SetTip(text, text.Text);
            return text;
        });
        ToolTip.SetTip(_previous, "Предыдущая страница");
        ToolTip.SetTip(_next, "Следующая страница");
        AutomationProperties.SetName(_search, "Поиск по всем полям");
        AutomationProperties.SetName(_sortColumn, "Столбец нативной сортировки");
        AutomationProperties.SetName(_editFilters, "Фильтры по типам, условий 0");
        ToolTip.SetTip(_sortColumn, "Нативная сортировка применяется провайдером ко всей выборке до разбиения на страницы. Недоступные столбцы исключены.");
        ToolTip.SetTip(_editFilters, "Условия AND применяются провайдером ко всей таблице до разбиения на страницы.");
        ToolTip.SetTip(_search, "Буквальный поиск по всем полям. Обработка регистра зависит от провайдера. Enter — применить.");
        _data.RowHeight = 34;
        _data.ColumnHeaderHeight = 38;
        _structure.RowHeight = 36;
        ConfigureStructureGrid();
        var queryBar = WrappedBar(_search, _sortColumn, _descending, _applyQuery, _editFilters, _clearQuery,
            _refreshTable, _cancelOperation, _showStructure);
        var actions = WrappedBar(_insertRow, _editCell, _deleteRow, _previous, _paging, _next);
        var dataArea = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8 };
        dataArea.Children.Add(_data);
        Add(dataArea, actions, 1);
        var inspectorContent = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        inspectorContent.Children.Add(new TextBlock { Text = "Структура", Classes = { "sectionTitle" } });
        Add(inspectorContent, _structure, 1);
        _inspector.Child = inspectorContent;
        _inspector.Width = 310;
        _inspector.Padding = new Thickness(10);
        _inspector.Bind(Border.BackgroundProperty, new DynamicResourceExtension("UiSurfaceMutedBrush"));
        _inspector.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("UiBorderBrush"));
        _inspector.BorderThickness = new Thickness(1);
        _inspector.CornerRadius = new CornerRadius(8);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        body.Children.Add(dataArea);
        Grid.SetColumn(_inspector, 1);
        body.Children.Add(_inspector);
        var detail = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 8 };
        detail.Children.Add(new StackPanel { Spacing = 3, Children = { _identity, _objectKind } });
        Add(detail, Surface(queryBar, "UiSurfaceMutedBrush", new Thickness(8, 6, 0, 0), 8), 1);
        Add(detail, _querySummary, 2);
        Add(detail, body, 3);
        Add(detail, new Expander { Header = "Особенности просмотра и ограничения", Content = new ScrollViewer { Content = _diagnostics, MaxHeight = 120 }, HorizontalAlignment = HorizontalAlignment.Stretch }, 4);
        Add(detail, new TextBlock { Text = "∅ курсивом означает NULL · двойной щелчок — полное значение", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, 5);
        _mode.Text = $"Только чтение · {providerLabel}";
        Add(detail, Surface(new StackPanel { Spacing = 4, Children = {
            new TextBlock { Text = $"{connectionName} · {providerLabel} · {endpoint}", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap },
            _status, _mode
        } }, "UiSurfaceMutedBrush", new Thickness(10, 6), 8), 6);
        Content = detail;
        ResetQuery();
        SetPresentationMode(WorkspacePresentationMode.Advanced);
        UpdateControls();
    }

    private static WrapPanel WrappedBar(params Control[] controls)
    {
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 6);
            if (control is TemplatedControl templated) templated.FontSize = 13;
            bar.Children.Add(control);
        }
        return bar;
    }

    public void SetPresentationMode(WorkspacePresentationMode mode)
    {
        _showStructure.IsChecked = mode == WorkspacePresentationMode.Advanced;
        _inspector.IsVisible = _showStructure.IsChecked == true;
    }

    public Task InitializeAsync() => _initialization ??= ConnectAsync();

    private static StackPanel IconLabel(string icon, string text) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center,
        Children = { UiIcon.Create(icon, 16), new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center } }
    };

    private static Border Surface(Control content, string resource, Thickness padding, double radius)
    {
        var border = new Border { Child = content, Padding = padding, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1) };
        border.Bind(Border.BackgroundProperty, new DynamicResourceExtension(resource));
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("UiBorderBrush"));
        return border;
    }

    private static void Add(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private void ConfigureStructureGrid()
    {
        foreach (var (label, binding, width) in new[]
        {
            ("№", nameof(ColumnRow.Position), 55),
            ("Поле", nameof(ColumnRow.Name), 200),
            ("Тип СУБД", nameof(ColumnRow.NativeType), 230),
            ("Допускает NULL", nameof(ColumnRow.Nullable), 120),
            ("Первичный ключ", nameof(ColumnRow.PrimaryKey), 130),
            ("Изменение ячейки", nameof(ColumnRow.Editability), 250),
            ("Новая запись", nameof(ColumnRow.Insertability), 280)
        })
            _structure.Columns.Add(new DataGridTextColumn
            {
                Header = label, Binding = new Binding(binding) { Mode = BindingMode.OneWay },
                Width = new DataGridLength(width)
            });
    }

    private async Task ConnectAsync()
    {
        if (_closed) return;
        await RunOperationAsync(async cancellationToken =>
        {
            var connect = _connectAsync ?? throw new InvalidOperationException("Фабрика подключения не настроена.");
            IDatabaseBrowser browser;
            try { browser = await connect(cancellationToken); }
            finally { _connectAsync = null; }
            if (_closed || cancellationToken.IsCancellationRequested)
            {
                await browser.DisposeAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            _browser = browser;
            _editor = browser is IDatabaseEditor { EditingEnabled: true } editor ? editor : null;
            _rowCreator = browser is IDatabaseRowCreator { EditingEnabled: true } creator ? creator : null;
            _rowDeleter = browser is IDatabaseRowDeleter { EditingEnabled: true } deleter ? deleter : null;
            _editCell.IsVisible = _editor is not null;
            _insertRow.IsVisible = _rowCreator is not null;
            _deleteRow.IsVisible = _rowDeleter is not null;
            _mode.Text = _editor is null && _rowCreator is null && _rowDeleter is null
                ? $"Только чтение · {_providerLabel}"
                : $"Безопасное редактирование включено · {_providerLabel}";
            await LoadTableCoreAsync(_table, 0, cancellationToken);
        }, "Подключение и загрузка таблицы…");
    }

    private void ResetQuery()
    {
        _query = new DatabaseBrowseQuery();
        _search.Text = string.Empty;
        _sortColumn.ItemsSource = new[] { new SortChoice(null) };
        _sortColumn.SelectedIndex = 0;
        _descending.IsChecked = false;
        UpdateFilterButton();
    }

    private async Task ApplyQueryAsync()
    {
        if (_busy || _closed)
            return;
        var query = new DatabaseBrowseQuery(_search.Text, (_sortColumn.SelectedItem as SortChoice)?.Name,
            _descending.IsChecked == true, _query.EffectiveFilters);
        try
        {
            query.Validate();
        }
        catch (ArgumentException exception)
        {
            _status.Text = exception.Message;
            return;
        }
        _query = query;
        await RunOperationAsync(cancellationToken => LoadTableCoreAsync(_table, 0, cancellationToken), "Поиск, фильтрация и сортировка данных…");
    }

    private async Task EditFiltersAsync()
    {
        if (_busy || _closed || _queryCapabilities is not { } capabilities || capabilities.Table != _table)
            return;

        IReadOnlyList<DatabaseFilter>? filters;
        try
        {
            filters = await ShowOwnedDialogAsync<IReadOnlyList<DatabaseFilter>?>(
                new DatabaseFilterDialog(capabilities, _query.EffectiveFilters));
        }
        catch (ArgumentException exception)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = exception.Message;
            return;
        }
        if (filters is null || _closed)
            return;

        var query = new DatabaseBrowseQuery(_search.Text, (_sortColumn.SelectedItem as SortChoice)?.Name,
            _descending.IsChecked == true, filters.ToArray());
        try
        {
            query.Validate();
        }
        catch (ArgumentException exception)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = exception.Message;
            return;
        }
        _query = query;
        UpdateFilterButton();
        await RunOperationAsync(cancellationToken => LoadTableCoreAsync(_table, 0, cancellationToken),
            "Применение фильтров ко всей таблице…");
    }

    private void UpdateFilterButton()
    {
        var count = _query.EffectiveFilters.Count;
        _editFilters.Content = $"Фильтры: {count}";
        AutomationProperties.SetName(_editFilters, $"Фильтры по типам, условий {count}");
    }

    private async Task RefreshTableAsync()
    {
        var offset = _page?.Offset ?? 0;
        await RunOperationAsync(cancellationToken => LoadTableCoreAsync(_table, offset, cancellationToken), "Обновление структуры и данных…");
    }

    private async Task ChangePageAsync(int delta)
    {
        if (_page is not { } page || _table != page.Table)
            return;
        if (delta > 0 && !page.HasMore || delta < 0 && page.Offset == 0)
            return;
        if (page.Offset > long.MaxValue - PageSize)
            return;
        var offset = Math.Max(0, page.Offset + delta);
        await RunOperationAsync(cancellationToken => LoadTableCoreAsync(_table, offset, cancellationToken), "Загрузка страницы…");
    }

    private async Task EditSelectedCellAsync()
    {
        if (_busy || _closed || _reloadRequiredBeforeWrite || _editor is null
            || _page is not { } page || _editCapabilities is not { CanUpdate: true } capabilities
            || capabilities.Table != page.Table || _data.SelectedItem is not DisplayRow { EditHandle: { } editHandle } row)
            return;
        if (!capabilities.Columns.Any(column => column.CanEdit))
            return;
        var editor = _editor;

        DatabaseCellEditChoice? choice;
        try
        {
            choice = await ShowOwnedDialogAsync<DatabaseCellEditChoice?>(new DatabaseCellEditDialog(capabilities, page.Columns, row.Values));
        }
        catch (ArgumentException exception)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = exception.Message;
            return;
        }
        if (choice is null || _closed)
            return;

        var table = page.Table;
        var offset = page.Offset;
        var request = new DatabaseCellUpdate(table, editHandle, choice.Column.Name, choice.Value);
        await RunOperationAsync(async cancellationToken =>
        {
            DatabaseUpdateResult updateResult;
            try
            {
                _reloadRequiredBeforeWrite = true;
                updateResult = await editor.UpdateCellAsync(request, cancellationToken);
            }
            catch (DatabaseBrowserException exception) when (IsEditConflict(exception))
            {
                if (_closed)
                    return;
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = exception.Message;
                if (await ConfirmReloadAsync("Запись изменилась · SmartSQL", "Изменение не применено",
                        exception.Message,
                        "SmartSQL не повторяет запись автоматически. Перезагрузите страницу, чтобы увидеть актуальные данные.")
                    && !_closed)
                    await ReloadAfterSpecialResultAsync(table, offset, cancellationToken,
                        "Изменение не применено. Страница обновлена.",
                        "Изменение не применено, но обновление страницы отменено. Обновите страницу перед следующим изменением.",
                        "Изменение не применено, но обновить страницу не удалось.");
                return;
            }
            catch (DatabaseBrowserException exception) when (IsCommitUnknown(exception))
            {
                if (_closed)
                    return;
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = exception.Message;
                if (await ConfirmReloadAsync("Результат записи неизвестен · SmartSQL", "Проверьте значение",
                        exception.Message,
                        "Не повторяйте изменение. Перезагрузите страницу и проверьте текущее значение ячейки.")
                    && !_closed)
                    await ReloadAfterSpecialResultAsync(table, offset, cancellationToken,
                        "Страница обновлена. Проверьте текущее значение: результат предыдущей записи был неизвестен.",
                        "Результат записи неизвестен, а обновление страницы отменено. Не повторяйте изменение; сначала обновите страницу.",
                        "Результат записи неизвестен, и обновить страницу не удалось. Не повторяйте изменение;");
                return;
            }
            catch (DatabaseBrowserException exception) when (RequiresReload(exception))
            {
                if (!_closed)
                    _reloadRequiredBeforeWrite = true;
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The provider may have committed before observing cancellation. Require an explicit reload
                // before another edit so a consumed or stale row handle is never reused.
                if (!_closed)
                    _reloadRequiredBeforeWrite = true;
                throw;
            }

            if (_closed)
                return;
            var providerMessage = string.IsNullOrWhiteSpace(updateResult.Message) ? string.Empty : " " + updateResult.Message;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await LoadTableCoreAsync(table, offset, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = "Ячейка сохранена, но обновление страницы отменено. Обновите страницу перед следующим изменением." + providerMessage;
                }
                return;
            }
            catch (DatabaseBrowserException exception)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = $"Ячейка сохранена, но обновить страницу не удалось. {exception.Message}" + providerMessage;
                }
                return;
            }
            catch (Exception)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = "Ячейка сохранена, но обновить страницу не удалось. Обновите страницу перед следующим изменением." + providerMessage;
                }
                return;
            }
            if (!_closed)
                _status.Text = $"Значение в столбце «{choice.Column.Name}» сохранено. Страница обновлена." + providerMessage;
        }, "Сохранение одной ячейки…");
    }

    private async Task InsertRowAsync()
    {
        if (_busy || _closed || _reloadRequiredBeforeWrite || _rowCreator is null
            || _page is not { } page
            || _insertCapabilities is not { CanInsert: true, ConfirmationHandle: { } confirmation } capabilities
            || confirmation == Guid.Empty || capabilities.Table != page.Table)
            return;
        var creator = _rowCreator;

        DatabaseInsertChoice? choice;
        try
        {
            choice = await ShowOwnedDialogAsync<DatabaseInsertChoice?>(new DatabaseInsertDialog(capabilities));
        }
        catch (ArgumentException exception)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = exception.Message;
            return;
        }
        if (choice is null || _closed)
            return;

        var table = page.Table;
        var offset = page.Offset;
        var request = new DatabaseInsertRequest(table, confirmation, choice.Values);
        await RunOperationAsync(async cancellationToken =>
        {
            try
            {
                _reloadRequiredBeforeWrite = true;
                await creator.InsertRowAsync(request, cancellationToken);
            }
            catch (DatabaseBrowserException exception) when (IsInsertCommitUnknown(exception))
            {
                if (_closed)
                    return;
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = exception.Message;
                if (await ConfirmReloadAsync("Результат добавления неизвестен · SmartSQL", "Проверьте новую запись",
                        exception.Message,
                        "Не повторяйте добавление. Перезагрузите страницу и проверьте, появилась ли запись.")
                    && !_closed)
                    await ReloadAfterSpecialResultAsync(table, offset, cancellationToken,
                        "Страница обновлена. Проверьте данные: результат предыдущего добавления был неизвестен.",
                        "Результат добавления неизвестен, а обновление страницы отменено. Не повторяйте добавление; сначала обновите страницу.",
                        "Результат добавления неизвестен, и обновить страницу не удалось. Не повторяйте добавление;");
                return;
            }
            catch (DatabaseBrowserException exception) when (InsertRequiresReload(exception))
            {
                if (!_closed)
                    _reloadRequiredBeforeWrite = true;
                throw;
            }

            if (_closed)
                return;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await LoadTableCoreAsync(table, offset, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = "Запись добавлена, но обновление страницы отменено. Обновите страницу перед следующей записью.";
                }
                return;
            }
            catch (DatabaseBrowserException exception)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = $"Запись добавлена, но обновить страницу не удалось. {exception.Message}";
                }
                return;
            }
            catch (Exception)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = "Запись добавлена, но обновить страницу не удалось. Обновите страницу перед следующей записью.";
                }
                return;
            }
            if (!_closed)
                _status.Text = "Запись добавлена. Страница обновлена.";
        }, "Добавление одной записи…");
    }

    private async Task DeleteSelectedRowAsync()
    {
        if (_busy || _closed || _deleteDialogOpen || _reloadRequiredBeforeWrite || _rowDeleter is null
            || _page is not { } page || _deleteCapabilities is not { CanDelete: true } capabilities
            || capabilities.Table != page.Table
            || _data.SelectedItem is not DisplayRow { DeleteHandle: { } deleteHandle } row
            || deleteHandle == Guid.Empty)
            return;
        var deleter = _rowDeleter;

        bool confirmed;
        _deleteDialogOpen = true;
        UpdateControls();
        try
        {
            confirmed = await ShowOwnedDialogAsync<bool>(new DatabaseRowDeleteDialog(_databaseName, page.Table, page.Columns, row.Values));
        }
        catch (ArgumentException exception)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = exception.Message;
            return;
        }
        finally
        {
            _deleteDialogOpen = false;
            if (!_closed)
                UpdateControls();
        }
        if (!confirmed || _closed)
            return;

        var table = page.Table;
        var offset = page.Offset;
        var successOffset = page.Rows.Count == 1 && !page.HasMore && offset > 0
            ? Math.Max(0, offset - PageSize)
            : offset;
        var request = new DatabaseRowDelete(table, deleteHandle);
        await RunOperationAsync(async cancellationToken =>
        {
            DatabaseDeleteResult result;
            try
            {
                _reloadRequiredBeforeWrite = true;
                result = await deleter.DeleteRowAsync(request, cancellationToken);
            }
            catch (DatabaseBrowserException exception) when (IsDeleteConflict(exception))
            {
                if (_closed)
                    return;
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = exception.Message;
                if (await ConfirmReloadAsync("Строка изменилась · SmartSQL", "Удаление не выполнено",
                        exception.Message,
                        "SmartSQL не повторяет удаление автоматически. Перезагрузите страницу и выберите строку заново.")
                    && !_closed)
                    await ReloadAfterSpecialResultAsync(table, offset, cancellationToken,
                        "Удаление не выполнено. Страница обновлена; выберите строку заново.",
                        "Удаление не выполнено, но обновление страницы отменено. Обновите страницу и выберите строку заново.",
                        "Удаление не выполнено, но обновить страницу не удалось.");
                return;
            }
            catch (DatabaseBrowserException exception) when (IsDeleteCommitUnknown(exception))
            {
                if (_closed)
                    return;
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = exception.Message;
                if (await ConfirmReloadAsync("Результат удаления неизвестен · SmartSQL", "Проверьте выбранную строку",
                        exception.Message,
                        "Не повторяйте удаление. Перезагрузите страницу и проверьте, осталась ли строка.")
                    && !_closed)
                    await ReloadAfterSpecialResultAsync(table, offset, cancellationToken,
                        "Страница обновлена. Проверьте данные: результат предыдущего удаления был неизвестен.",
                        "Результат удаления неизвестен, а обновление страницы отменено. Не повторяйте удаление; сначала обновите страницу.",
                        "Результат удаления неизвестен, и обновить страницу не удалось. Не повторяйте удаление;");
                return;
            }
            catch (DatabaseBrowserException exception) when (DeleteRequiresReload(exception))
            {
                if (!_closed)
                    _reloadRequiredBeforeWrite = true;
                throw;
            }
            catch (DatabaseBrowserException)
            {
                // A submitted delete handle is single-use even when the transaction rolls back.
                if (!_closed)
                    _reloadRequiredBeforeWrite = true;
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Text = "Удаление отменено. SmartSQL не повторяет операцию. Обновите страницу и выберите строку заново.";
                }
                return;
            }
            catch (Exception)
            {
                if (!_closed)
                    _reloadRequiredBeforeWrite = true;
                throw;
            }

            if (_closed)
                return;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await LoadTableCoreAsync(table, successOffset, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = "Строка удалена, но обновление страницы отменено. Обновите страницу перед следующей операцией.";
                }
                return;
            }
            catch (DatabaseBrowserException exception)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = $"Строка удалена, но обновить страницу не удалось. {exception.Message}";
                }
                return;
            }
            catch (Exception)
            {
                if (!_closed)
                {
                    _reloadRequiredBeforeWrite = true;
                    _status.Foreground = Brushes.Firebrick;
                    _status.Text = "Строка удалена, но обновить страницу не удалось. Обновите страницу перед следующей операцией.";
                }
                return;
            }
            if (!_closed)
            {
                _status.ClearValue(TextBlock.ForegroundProperty);
                _status.Text = string.IsNullOrWhiteSpace(result.Message)
                    ? "Строка удалена. Страница обновлена."
                    : $"Строка удалена. Страница обновлена. {result.Message}";
            }
        }, "Удаление одной выбранной строки…");
    }

    private async Task<bool> ConfirmReloadAsync(string title, string heading, string message, string guidance)
    {
        var dialog = new Window
        {
            Title = title, Width = 470, Height = 230,
            MinWidth = 420, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Classes = { "dialog" }
        };
        var reload = new Button { Content = "Перезагрузить страницу", Classes = { "primary" } };
        var keep = new Button { Content = "Оставить как есть", Classes = { "quiet" } };
        reload.Click += (_, _) => dialog.Close(true);
        keep.Click += (_, _) => dialog.Close(false);
        var explanation = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = heading, Classes = { "sectionTitle" } },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = guidance,
                    Classes = { "caption" }, TextWrapping = TextWrapping.Wrap
                }
            }
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { keep, reload }
        };
        var layout = new Grid
        {
            Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 16
        };
        layout.Children.Add(explanation);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        dialog.Content = layout;
        return await ShowOwnedDialogAsync<bool>(dialog);
    }

    private async Task ReloadAfterSpecialResultAsync(DatabaseTableRef table, long offset,
        CancellationToken cancellationToken, string successMessage, string canceledMessage, string failurePrefix)
    {
        try
        {
            await LoadTableCoreAsync(table, offset, cancellationToken);
            if (!_closed)
            {
                _status.ClearValue(TextBlock.ForegroundProperty);
                _status.Text = successMessage;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_closed)
            {
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = canceledMessage;
            }
        }
        catch (DatabaseBrowserException exception)
        {
            if (!_closed)
            {
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = $"{failurePrefix} {exception.Message}";
            }
        }
        catch (Exception)
        {
            if (!_closed)
            {
                _reloadRequiredBeforeWrite = true;
                _status.Foreground = Brushes.Firebrick;
                _status.Text = $"{failurePrefix} Обновите страницу перед следующей безопасной операцией.";
            }
        }
    }

    private static bool IsEditConflict(DatabaseBrowserException exception) =>
        exception.Code is "edit_conflict" or "update_conflict" or "concurrent_update";

    private static bool IsCommitUnknown(DatabaseBrowserException exception) =>
        exception.Code == "edit_commit_unknown";

    private static bool RequiresReload(DatabaseBrowserException exception) =>
        exception.Code is "edit_handle_expired" or "edit_schema_changed" or "edit_not_allowed";

    private static bool IsInsertCommitUnknown(DatabaseBrowserException exception) =>
        exception.Code == "insert_commit_unknown";

    private static bool InsertRequiresReload(DatabaseBrowserException exception) =>
        exception.Code is "insert_confirmation_expired" or "insert_schema_changed" or "insert_not_allowed";

    private static bool IsDeleteConflict(DatabaseBrowserException exception) =>
        exception.Code == "delete_conflict";

    private static bool IsDeleteCommitUnknown(DatabaseBrowserException exception) =>
        exception.Code == "delete_commit_unknown";

    private static bool DeleteRequiresReload(DatabaseBrowserException exception) =>
        exception.Code is "delete_handle_expired" or "delete_schema_changed" or "delete_not_allowed" or "delete_referenced";

    private async Task LoadTableCoreAsync(DatabaseTableRef table, long offset, CancellationToken cancellationToken)
    {
        if (_browser is null)
            return;
        ClearTable();
        _identity.Text = table.Name;
        _objectKind.Text = $"{table.Schema}  /  {KindLabel(table.Kind)} · "
            + (_editor is null && _rowCreator is null && _rowDeleter is null
                ? "только чтение" : "проверка безопасных операций");
        ToolTip.SetTip(_identity, $"{table.Schema}.{table.Name}");
        var columns = await _browser.GetColumnsAsync(table, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_closed)
            return;
        var queryCapabilities = await _browser.GetQueryCapabilitiesAsync(table, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_closed)
            return;
        if (queryCapabilities.Table != table)
            throw new InvalidOperationException("Провайдер вернул возможности запросов другого объекта.");
        DatabaseEditCapabilities? capabilities = null;
        if (_editor is not null)
        {
            capabilities = await _editor.GetEditCapabilitiesAsync(table, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed)
                return;
            if (capabilities.Table != table)
                throw new InvalidOperationException("Провайдер вернул возможности редактирования другого объекта.");
        }
        DatabaseInsertCapabilities? insertCapabilities = null;
        if (_rowCreator is not null)
        {
            insertCapabilities = await _rowCreator.GetInsertCapabilitiesAsync(table, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed)
                return;
            if (insertCapabilities.Table != table)
                throw new InvalidOperationException("Провайдер вернул возможности добавления для другого объекта.");
            if (insertCapabilities.CanInsert
                && (insertCapabilities.ConfirmationHandle is not { } handle || handle == Guid.Empty))
                throw new InvalidOperationException("Провайдер не подтвердил метаданные для добавления записи.");
        }
        DatabaseDeleteCapabilities? deleteCapabilities = null;
        if (_rowDeleter is not null)
        {
            deleteCapabilities = await _rowDeleter.GetDeleteCapabilitiesAsync(table, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed)
                return;
            if (deleteCapabilities.Table != table)
                throw new InvalidOperationException("Провайдер вернул возможности удаления для другого объекта.");
        }
        _editCapabilities = capabilities;
        _insertCapabilities = insertCapabilities;
        _deleteCapabilities = deleteCapabilities;
        _queryCapabilities = queryCapabilities;
        ShowStructure(columns, capabilities, insertCapabilities);
        var page = await _browser.ReadPageAsync(table, offset, PageSize, _query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_closed)
            return;
        if (page.Table != table || page.Offset != offset || page.PageSize != PageSize)
            throw new InvalidOperationException("Провайдер вернул страницу другого объекта.");
        if (deleteCapabilities is { CanDelete: true }
            && page.Rows.Any(row => row.DeleteHandle is not { } handle || handle == Guid.Empty))
            throw new InvalidOperationException("Провайдер не подтвердил удаление для каждой строки текущей страницы.");
        _page = page;
        var canUpdate = capabilities is { CanUpdate: true };
        var canInsert = insertCapabilities is { CanInsert: true };
        var canDelete = deleteCapabilities is { CanDelete: true };
        _objectKind.Text = $"{table.Schema}  /  {KindLabel(table.Kind)} · " + ((canUpdate, canInsert, canDelete) switch
        {
            (true, true, true) => "можно добавлять, изменять и удалять строки",
            (true, true, false) => "можно добавлять записи и изменять ячейки",
            (true, false, true) => "можно изменять ячейки и удалять строки",
            (false, true, true) => "можно добавлять и удалять строки",
            (true, false, false) => "можно изменить одну ячейку",
            (false, true, false) => "можно добавить одну запись",
            (false, false, true) => "можно удалить выбранную строку",
            _ => "только чтение для этого объекта"
        });
        var queryParts = new List<string>();
        if (_query.EffectiveFilters.Count > 0)
            queryParts.Add($"Фильтры: {_query.EffectiveFilters.Count} условий AND ко всей таблице до страниц");
        if (!string.IsNullOrEmpty(_query.SearchText))
            queryParts.Add($"поиск по всей таблице: {_query.SearchText}");
        queryParts.Add(_query.SortColumn is { } sort
            ? $"нативная сортировка всей выборки: {sort} {(_query.Descending ? "↓" : "↑")}"
            : "исходный порядок провайдера");
        _querySummary.Text = string.Join(" · ", queryParts);
        ToolTip.SetTip(_querySummary, _querySummary.Text);
        ShowStructure(page.Columns, capabilities, insertCapabilities);
        var choices = new[] { new SortChoice(null) }.Concat(queryCapabilities.Columns
            .Where(column => column.CanSort)
            .Select(column => new SortChoice(column.Name))).ToArray();
        _sortColumn.ItemsSource = choices;
        _sortColumn.SelectedItem = choices.FirstOrDefault(choice => choice.Name == _query.SortColumn) ?? choices[0];
        UpdateFilterButton();
        _data.Columns.Clear();
        for (var index = 0; index < page.Columns.Count; index++)
        {
            var columnIndex = index;
            var fieldName = page.Columns[index].Name;
            _data.Columns.Add(new DataGridTemplateColumn
            {
                Header = ColumnHeader(page.Columns[index]),
                Width = new DataGridLength(180),
                CanUserSort = false,
                CellTemplate = new FuncDataTemplate<DisplayRow>((row, _) =>
                {
                    var value = row?.Values[columnIndex];
                    var text = new TextBlock
                    {
                        Text = value ?? "∅", Margin = new Thickness(4, 0), FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    if (value is null)
                    {
                        text.Opacity = 0.55;
                        text.FontStyle = FontStyle.Italic;
                        ToolTip.SetTip(text, "NULL — отсутствующее значение");
                    }
                    text.DoubleTapped += async (_, args) =>
                    {
                        args.Handled = true;
                        await ShowOwnedDialogAsync<object?>(new CellValueDialog(fieldName, value));
                    };
                    return text;
                })
            });
        }
        _data.ItemsSource = page.Rows.Select(row =>
            new DisplayRow(row.Values.ToArray(), row.EditHandle, row.DeleteHandle)).ToArray();
        _paging.Text = page.Rows.Count == 0
            ? $"Страница {page.Offset / PageSize + 1}: записей нет"
            : $"Страница {page.Offset / PageSize + 1} · записи {page.Offset + 1}–{page.Offset + page.Rows.Count}";
        var diagnostics = page.Diagnostics.Select(diagnostic => diagnostic.Message).ToList();
        if (capabilities is not null)
            diagnostics.AddRange(capabilities.Diagnostics.Select(diagnostic => diagnostic.Message));
        if (insertCapabilities is not null)
            diagnostics.AddRange(insertCapabilities.Diagnostics.Select(diagnostic => diagnostic.Message));
        if (deleteCapabilities is not null)
            diagnostics.AddRange(deleteCapabilities.Diagnostics.Select(diagnostic => diagnostic.Message));
        diagnostics.AddRange(queryCapabilities.Diagnostics.Select(diagnostic => diagnostic.Message));
        diagnostics.AddRange(queryCapabilities.Columns.Where(column => !string.IsNullOrWhiteSpace(column.Limitation))
            .Select(column => $"{column.Name}: {column.Limitation}"));
        if (!page.Columns.Any(column => column.PrimaryKeyOrder is not null) && !diagnostics.Any(message => message.Contains("ключ", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add("У объекта нет первичного ключа: при изменении данных между загрузками записи на страницах могут повторяться или пропускаться.");
        _diagnostics.Text = string.Join("\n", diagnostics.Distinct(StringComparer.Ordinal));
        _status.Text = (canUpdate, canInsert, canDelete) switch
        {
            (true, true, true) => $"Загружено записей: {page.Rows.Count}. Можно добавить запись, изменить ячейку или удалить выбранную строку.",
            (true, true, false) => $"Загружено записей: {page.Rows.Count}. Можно добавить запись или изменить ячейку выбранной строки.",
            (true, false, true) => $"Загружено записей: {page.Rows.Count}. Выберите строку для изменения или удаления.",
            (false, true, true) => $"Загружено записей: {page.Rows.Count}. Можно добавить запись или удалить выбранную строку.",
            (true, false, false) => $"Загружено записей: {page.Rows.Count}. Выберите строку и нажмите «Изменить значение».",
            (false, true, false) => $"Загружено записей: {page.Rows.Count}. Можно добавить одну запись.",
            (false, false, true) => $"Загружено записей: {page.Rows.Count}. Выберите строку для удаления.",
            _ => $"Загружено записей: {page.Rows.Count}. Данные доступны только для просмотра."
        };
    }

    private void ShowStructure(IReadOnlyList<DatabaseColumn> columns, DatabaseEditCapabilities? capabilities,
        DatabaseInsertCapabilities? insertCapabilities)
    {
        var editColumns = capabilities?.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, EditableDatabaseColumn>(StringComparer.Ordinal);
        var insertColumns = insertCapabilities?.Columns.ToDictionary(column => column.Column.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, InsertableDatabaseColumn>(StringComparer.Ordinal);
        _structure.ItemsSource = columns.Select(column => new ColumnRow(column.Ordinal + 1, column.Name,
            column.NativeType, column.IsNullable ? "Да" : "Нет",
            column.PrimaryKeyOrder is { } order ? $"PK · {order}" : string.Empty,
            editColumns.TryGetValue(column.Name, out var editable)
                ? editable.CanEdit ? "Можно изменять" : editable.ReadOnlyReason ?? "Только чтение"
                : _editor is null ? "Изменение не включено" : "Только чтение",
            insertColumns.TryGetValue(column.Name, out var insertable)
                ? DescribeInsert(insertable)
                : _rowCreator is null ? "Добавление не включено" : "Только чтение")).ToArray();
    }

    private static string DescribeInsert(InsertableDatabaseColumn column)
    {
        if (column.MustUseDefault)
            return column.DefaultDescription is { Length: > 0 }
                ? $"Заполнит сервер · {column.DefaultDescription}"
                : "Заполнит сервер";
        var choices = new List<string> { "значение" };
        if (column.Column.IsNullable)
            choices.Add("NULL");
        if (column.CanUseDefault)
            choices.Add("сервер");
        var description = string.Join(" / ", choices);
        return column.ReadOnlyReason is { Length: > 0 }
            ? $"{description} · {column.ReadOnlyReason}"
            : description;
    }

    private void ClearTable()
    {
        _page = null;
        _editCapabilities = null;
        _insertCapabilities = null;
        _deleteCapabilities = null;
        _queryCapabilities = null;
        _reloadRequiredBeforeWrite = false;
        _data.SelectedItem = null;
        _data.ItemsSource = null;
        _data.Columns.Clear();
        _structure.ItemsSource = null;
        _identity.Text = _table.Name;
        _objectKind.Text = $"{_table.Schema} / {KindLabel(_table.Kind)} · "
            + (_editor is null && _rowCreator is null && _rowDeleter is null
                ? "только чтение" : "безопасные операции по подтверждённым возможностям");
        ToolTip.SetTip(_identity, $"{_table.Schema}.{_table.Name}");
        _querySummary.Text = string.Empty;
        _diagnostics.Text = string.Empty;
        _paging.Text = "По 100 записей на странице";
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action, string progress)
    {
        if (_busy || _closed)
            return;
        _busy = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operationCompletion = completion;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        _status.ClearValue(TextBlock.ForegroundProperty);
        _status.Text = progress;
        UpdateControls();
        try
        {
            await action(operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (!_closed)
                _status.Text = _browser is null
                    ? "Подключение отменено. Откройте новое подключение, чтобы повторить."
                    : _reloadRequiredBeforeWrite
                        ? "Операция отменена. Результат записи может быть неизвестен; обновите страницу перед следующим изменением."
                        : "Операция отменена. SmartSQL не повторяет изменения автоматически.";
        }
        catch (DatabaseBrowserException exception)
        {
            if (!_closed)
            {
                _status.Foreground = Brushes.Firebrick;
                _status.Text = exception.Message;
            }
        }
        catch (Exception)
        {
            if (!_closed)
            {
                _status.Foreground = Brushes.Firebrick;
                _status.Text = "Не удалось получить данные. Проверьте подключение, права пользователя и доступность выбранного объекта.";
            }
        }
        finally
        {
            _operation = null;
            _busy = false;
            if (!_closed)
                UpdateControls();
            completion.TrySetResult();
        }
    }

    private void UpdateControls()
    {
        var connected = _browser is not null;
        var queryEnabled = connected && !_busy && !_closed;
        _search.IsEnabled = queryEnabled;
        _sortColumn.IsEnabled = queryEnabled;
        _descending.IsEnabled = queryEnabled;
        _applyQuery.IsEnabled = queryEnabled;
        _editFilters.IsEnabled = queryEnabled && _queryCapabilities is { } queryCapabilities
            && queryCapabilities.Table == _table;
        _clearQuery.IsEnabled = queryEnabled;
        _refreshTable.IsEnabled = connected && !_busy && !_closed;
        _previous.IsEnabled = !_busy && _page is { Offset: > 0 };
        _next.IsEnabled = !_busy && _page is { HasMore: true };
        _editCell.IsEnabled = !_busy && !_reloadRequiredBeforeWrite && _editor is not null
            && _page is { } page && _editCapabilities is { CanUpdate: true } capabilities
            && capabilities.Table == page.Table && capabilities.Columns.Any(column => column.CanEdit)
            && _data.SelectedItem is DisplayRow { EditHandle: not null };
        _insertRow.IsEnabled = !_busy && !_reloadRequiredBeforeWrite && _rowCreator is not null
            && _page is { } insertPage
            && _insertCapabilities is { CanInsert: true, ConfirmationHandle: { } confirmation } insert
            && confirmation != Guid.Empty && insert.Table == insertPage.Table;
        _deleteRow.IsEnabled = !_busy && !_deleteDialogOpen && !_reloadRequiredBeforeWrite && _rowDeleter is not null
            && _page is { } deletePage && _deleteCapabilities is { CanDelete: true } deletion
            && deletion.Table == deletePage.Table
            && _data.SelectedItem is DisplayRow { DeleteHandle: { } deleteHandle }
            && deleteHandle != Guid.Empty;
        _cancelOperation.IsEnabled = _busy;
    }

    private async Task<T?> ShowOwnedDialogAsync<T>(Window dialog)
    {
        if (_closed || TopLevel.GetTopLevel(this) is not Window owner) return default;
        _dialogs.Add(dialog);
        try { return await dialog.ShowDialog<T>(owner); }
        finally { _dialogs.Remove(dialog); }
    }

    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        _closed = true;
        _connectAsync = null;
        var cancellation = _lifetime.CancelAsync();
        foreach (var dialog in _dialogs.ToArray()) dialog.Close();
        try { await cancellation; }
        catch (AggregateException) { /* Cancellation is requested even if a provider callback fails. */ }
        if (_operationCompletion is { } completion) await completion.Task;
        var browser = _browser;
        _browser = null;
        _editor = null;
        _rowCreator = null;
        _rowDeleter = null;
        ClearTable();
        IsEnabled = false;
        if (browser is not null)
        {
            try { await browser.DisposeAsync(); }
            catch (Exception) { /* Cleanup must not expose provider credentials. */ }
        }
        _dialogs.Clear();
        _lifetime.Dispose();
    }

    private static string KindLabel(DatabaseObjectKind kind) => kind switch
    {
        DatabaseObjectKind.Table => "Таблица",
        DatabaseObjectKind.View => "Представление",
        DatabaseObjectKind.MaterializedView => "Материализованное представление",
        DatabaseObjectKind.ForeignTable => "Внешняя таблица",
        DatabaseObjectKind.PartitionedTable => "Секционированная таблица",
        _ => "Объект данных"
    };

    private static Control ColumnHeader(DatabaseColumn column)
    {
        var header = new TextBlock { Text = column.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(header, $"{column.Name}\n{column.NativeType}" + (column.PrimaryKeyOrder is { } key ? $" · PK {key}" : string.Empty));
        return header;
    }
}
