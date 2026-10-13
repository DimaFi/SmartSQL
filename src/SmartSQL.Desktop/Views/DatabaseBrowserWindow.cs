using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Views;

/// <summary>Provider-neutral read-only database browser; composition supplies the connection factory.</summary>
public sealed class DatabaseBrowserWindow : Window
{
    private const int PageSize = 100;
    private readonly ConnectionProfile _profile;
    private readonly Func<ConnectionProfile, string, CancellationToken, Task<IDatabaseBrowser>> _connectAsync;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ComboBox _schemas = new();
    private readonly ListBox _tables = new();
    private readonly DataGrid _data = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly DataGrid _structure = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBlock _identity = new() { Text = "Выберите таблицу", FontSize = 19, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _diagnostics = new() { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _paging = new() { Text = "По 100 записей на странице", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous = new() { Content = "← Предыдущая", IsEnabled = false };
    private readonly Button _next = new() { Content = "Следующая →", IsEnabled = false };
    private readonly Button _refreshTable = new() { Content = "Обновить данные", IsEnabled = false };
    private readonly Button _refreshCatalog = new() { Content = "Обновить список", IsEnabled = false };
    private readonly Button _cancelOperation = new() { Content = "Отменить загрузку", IsEnabled = false };
    private IReadOnlyList<DatabaseTableRef> _catalog = [];
    private IDatabaseBrowser? _browser;
    private DatabasePage? _page;
    private CancellationTokenSource? _operation;
    private string _password;
    private bool _busy;
    private bool _closed;
    private bool _ignoreSelections;
    private bool _connectionStarted;

    private sealed record SchemaChoice(string? Name)
    {
        public override string ToString() => Name ?? "Все схемы";
    }

    private sealed record TableChoice(DatabaseTableRef Table)
    {
        public override string ToString() => $"{Table.Schema} · {Table.Name}\n{KindLabel(Table.Kind)}";
    }

    private sealed record DisplayRow(IReadOnlyList<string?> Values);
    private sealed record ColumnRow(int Position, string Name, string NativeType, string Nullable, string PrimaryKey);

    public DatabaseBrowserWindow(ConnectionProfile profile, string password,
        Func<ConnectionProfile, string, CancellationToken, Task<IDatabaseBrowser>> connectAsync)
    {
        _profile = profile;
        _password = password;
        _connectAsync = connectAsync;
        Title = $"{profile.Name} · PostgreSQL · SmartSQL";
        Width = 1160;
        Height = 760;
        MinWidth = 850;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _schemas.SelectionChanged += (_, _) => FilterTables();
        _tables.SelectionChanged += async (_, _) => await TableSelectedAsync();
        _previous.Click += async (_, _) => await ChangePageAsync(-PageSize);
        _next.Click += async (_, _) => await ChangePageAsync(PageSize);
        _refreshTable.Click += async (_, _) => await RefreshTableAsync();
        _refreshCatalog.Click += async (_, _) => await RefreshCatalogAsync();
        _cancelOperation.Click += (_, _) =>
        {
            _operation?.Cancel();
            _status.Text = "Отмена загрузки…";
        };
        var disconnect = new Button { Content = "Отключиться" };
        disconnect.Click += (_, _) => Close();
        Opened += async (_, _) => await ConnectAsync();
        Closed += async (_, _) => await ShutdownAsync();

        var navigator = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 10 };
        navigator.Children.Add(new TextBlock { Text = "Схемы и таблицы", FontWeight = FontWeight.SemiBold });
        Add(navigator, _schemas, 1);
        Add(navigator, _tables, 2);
        Add(navigator, _refreshCatalog, 3);
        var dataTab = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10 };
        dataTab.Children.Add(_data);
        Add(dataTab, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { _previous, _paging, _next }
        }, 1);
        ConfigureStructureGrid();
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Данные", Content = dataTab },
                new TabItem { Header = "Структура", Content = _structure }
            }
        };
        var detail = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 12 };
        detail.Children.Add(_identity);
        Add(detail, tabs, 1);
        Add(detail, _diagnostics, 2);
        Add(detail, new TextBlock { Text = "Режим просмотра · ∅ курсивом означает NULL · страницы по 100 записей", Opacity = 0.7 }, 3);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("275,*"), ColumnSpacing = 18 };
        body.Children.Add(navigator);
        Grid.SetColumn(detail, 1);
        body.Children.Add(detail);
        var layout = new Grid { Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
        layout.Children.Add(new TextBlock
        {
            Text = $"{profile.Name} · {profile.Host}:{profile.Port} · база {profile.Database}",
            FontSize = 23, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        Add(layout, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _refreshTable, _cancelOperation, disconnect }
        }, 1);
        Add(layout, body, 2);
        Add(layout, _status, 3);
        Content = layout;
        UpdateControls();
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
            ("Первичный ключ", nameof(ColumnRow.PrimaryKey), 130)
        })
            _structure.Columns.Add(new DataGridTextColumn
            {
                Header = label, Binding = new Binding(binding) { Mode = BindingMode.OneWay },
                Width = new DataGridLength(width)
            });
    }

    private async Task ConnectAsync()
    {
        if (_connectionStarted || _closed)
            return;
        _connectionStarted = true;
        var password = _password;
        _password = string.Empty;
        await RunOperationAsync(async cancellationToken =>
        {
            var browser = await _connectAsync(_profile, password, cancellationToken);
            if (_closed || cancellationToken.IsCancellationRequested)
            {
                await browser.DisposeAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            _browser = browser;
            await LoadCatalogCoreAsync(cancellationToken);
        }, "Подключение и загрузка списка таблиц…");
    }

    private async Task RefreshCatalogAsync() => await RunOperationAsync(LoadCatalogCoreAsync, "Обновление списка таблиц…");

    private async Task LoadCatalogCoreAsync(CancellationToken cancellationToken)
    {
        if (_browser is null)
            return;
        var catalog = await _browser.GetTablesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_closed)
            return;
        _catalog = catalog.OrderBy(table => table.Schema, StringComparer.Ordinal)
            .ThenBy(table => table.Name, StringComparer.Ordinal).ToArray();
        _ignoreSelections = true;
        try
        {
            _schemas.ItemsSource = new[] { new SchemaChoice(null) }
                .Concat(_catalog.Select(table => table.Schema).Distinct(StringComparer.Ordinal).Select(name => new SchemaChoice(name))).ToArray();
            _schemas.SelectedIndex = 0;
            PopulateTableList();
        }
        finally
        {
            _ignoreSelections = false;
        }
        _status.Text = _catalog.Count == 0
            ? "Доступных таблиц и представлений не найдено. Проверьте права пользователя."
            : $"Подключено. Доступно объектов: {_catalog.Count}. Выберите таблицу слева.";
    }

    private void FilterTables()
    {
        if (_ignoreSelections || _busy || _closed)
            return;
        PopulateTableList();
        UpdateControls();
    }

    private void PopulateTableList()
    {
        var schema = (_schemas.SelectedItem as SchemaChoice)?.Name;
        _tables.ItemsSource = _catalog.Where(table => schema is null || table.Schema == schema)
            .Select(table => new TableChoice(table)).ToArray();
        _tables.SelectedItem = null;
        ClearTable();
    }

    private async Task TableSelectedAsync()
    {
        if (_ignoreSelections || _busy || _closed)
            return;
        if (_tables.SelectedItem is not TableChoice choice)
        {
            ClearTable();
            UpdateControls();
            return;
        }
        ClearTable();
        await RunOperationAsync(cancellationToken => LoadTableCoreAsync(choice.Table, 0, cancellationToken), "Загрузка структуры и первой страницы…");
    }

    private async Task RefreshTableAsync()
    {
        if (_tables.SelectedItem is not TableChoice choice)
            return;
        var offset = _page?.Offset ?? 0;
        await RunOperationAsync(cancellationToken => LoadTableCoreAsync(choice.Table, offset, cancellationToken), "Обновление структуры и данных…");
    }

    private async Task ChangePageAsync(int delta)
    {
        if (_page is not { } page || _tables.SelectedItem is not TableChoice choice || choice.Table != page.Table)
            return;
        if (delta > 0 && !page.HasMore || delta < 0 && page.Offset == 0)
            return;
        if (page.Offset > long.MaxValue - PageSize)
            return;
        var offset = Math.Max(0, page.Offset + delta);
        await RunOperationAsync(cancellationToken => LoadTableCoreAsync(choice.Table, offset, cancellationToken), "Загрузка страницы…");
    }

    private async Task LoadTableCoreAsync(DatabaseTableRef table, long offset, CancellationToken cancellationToken)
    {
        if (_browser is null)
            return;
        ClearTable();
        _identity.Text = $"Схема: {table.Schema} · объект: {table.Name} · {KindLabel(table.Kind)}";
        var columns = await _browser.GetColumnsAsync(table, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_closed)
            return;
        ShowStructure(columns);
        var page = await _browser.ReadPageAsync(table, offset, PageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_closed)
            return;
        if (page.Table != table || page.Offset != offset || page.PageSize != PageSize)
            throw new InvalidOperationException("Провайдер вернул страницу другого объекта.");
        _page = page;
        ShowStructure(page.Columns);
        _data.Columns.Clear();
        for (var index = 0; index < page.Columns.Count; index++)
        {
            var columnIndex = index;
            _data.Columns.Add(new DataGridTemplateColumn
            {
                Header = page.Columns[index].Name,
                Width = new DataGridLength(180),
                CanUserSort = false,
                CellTemplate = new FuncDataTemplate<DisplayRow>((row, _) =>
                {
                    var value = row?.Values[columnIndex];
                    var text = new TextBlock
                    {
                        Text = value ?? "∅", Margin = new Thickness(6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    if (value is null)
                    {
                        text.Opacity = 0.55;
                        text.FontStyle = FontStyle.Italic;
                        ToolTip.SetTip(text, "NULL — отсутствующее значение");
                    }
                    return text;
                })
            });
        }
        _data.ItemsSource = page.Rows.Select(row => new DisplayRow(row.Values.ToArray())).ToArray();
        _paging.Text = page.Rows.Count == 0
            ? $"Страница {page.Offset / PageSize + 1}: записей нет"
            : $"Страница {page.Offset / PageSize + 1} · записи {page.Offset + 1}–{page.Offset + page.Rows.Count}";
        var diagnostics = page.Diagnostics.Select(diagnostic => diagnostic.Message).ToList();
        if (!page.Columns.Any(column => column.PrimaryKeyOrder is not null) && !diagnostics.Any(message => message.Contains("ключ", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add("У объекта нет первичного ключа: при изменении данных между загрузками записи на страницах могут повторяться или пропускаться.");
        _diagnostics.Text = string.Join("\n", diagnostics);
        _status.Text = $"Загружено записей: {page.Rows.Count}. Данные доступны только для просмотра.";
    }

    private void ShowStructure(IReadOnlyList<DatabaseColumn> columns)
    {
        _structure.ItemsSource = columns.Select(column => new ColumnRow(column.Ordinal + 1, column.Name,
            column.NativeType, column.IsNullable ? "Да" : "Нет",
            column.PrimaryKeyOrder is { } order ? $"PK · {order}" : string.Empty)).ToArray();
    }

    private void ClearTable()
    {
        _page = null;
        _data.ItemsSource = null;
        _data.Columns.Clear();
        _structure.ItemsSource = null;
        _identity.Text = "Выберите таблицу";
        _diagnostics.Text = string.Empty;
        _paging.Text = "По 100 записей на странице";
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action, string progress)
    {
        if (_busy || _closed)
            return;
        _busy = true;
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
                _status.Text = _browser is null ? "Подключение отменено. Откройте новое подключение, чтобы повторить." : "Загрузка отменена.";
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
        }
    }

    private void UpdateControls()
    {
        var connected = _browser is not null;
        _schemas.IsEnabled = connected && !_busy;
        _tables.IsEnabled = connected && !_busy;
        _refreshCatalog.IsEnabled = connected && !_busy;
        _refreshTable.IsEnabled = connected && !_busy && _tables.SelectedItem is TableChoice;
        _previous.IsEnabled = !_busy && _page is { Offset: > 0 };
        _next.IsEnabled = !_busy && _page is { HasMore: true };
        _cancelOperation.IsEnabled = _busy;
    }

    private async Task ShutdownAsync()
    {
        if (_closed)
            return;
        _closed = true;
        _password = string.Empty;
        _lifetime.Cancel();
        _operation?.Cancel();
        var browser = _browser;
        _browser = null;
        if (browser is not null)
        {
            try
            {
                await browser.DisposeAsync();
            }
            catch (Exception)
            {
                // The window is closed; never expose provider credentials through cleanup errors.
            }
        }
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
}
