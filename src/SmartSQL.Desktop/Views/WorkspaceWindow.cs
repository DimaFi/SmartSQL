using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SmartSQL.Application;
using SmartSQL.Desktop.Presentation;
using SmartSQL.Desktop.Workspaces;
using SmartSQL.Domain;
using SmartSQL.Infrastructure;

namespace SmartSQL.Desktop.Views;

/// <summary>One desktop workspace. Connections own credentials; table tabs own independent provider sessions.</summary>
public sealed class WorkspaceWindow : Window
{
    private readonly ObservableCollection<TreeViewItem> _roots = [];
    private readonly ObservableCollection<TabItem> _tabItems = [];
    private readonly List<ConnectionEntry> _connections = [];
    private readonly List<WorkspaceTab> _openTabs = [];
    private readonly TreeView _tree = new();
    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _context = new() { Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ToggleSwitch _mode = new() { OffContent = "Простой режим", OnContent = "Расширенный режим", IsEnabled = false };
    private readonly TextBlock _empty = new()
    {
        Text = "Выберите таблицу в дереве подключений и нажмите Enter или откройте её двойным щелчком.",
        TextWrapping = TextWrapping.Wrap, MaxWidth = 430, Margin = new Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private IWorkspacePreferencesStore? _preferences;
    private WorkspacePresentationMode _presentationMode;
    private Task _preferenceSave = Task.CompletedTask;
    private bool _settingMode;
    private bool _starting = true;
    private bool _dialogOpen;
    private bool _shuttingDown;
    private bool _shutdownFinished;
    private int _connectionNumber;

    private sealed class ConnectionEntry
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public required string Name { get; init; }
        public required string Endpoint { get; init; }
        public required string Provider { get; init; }
        public required string Database { get; init; }
        public required TreeViewItem Node { get; init; }
        public WorkspaceConnection? Native { get; init; }
        public string? ManagedPath { get; init; }
        public bool Closing { get; set; }
        public bool Refreshing { get; set; }
        public IReadOnlyList<TableDefinition> ManagedTables { get; set; } = [];
    }

    private sealed record NativeTarget(ConnectionEntry Connection, DatabaseTableRef Table);
    private sealed record ManagedTarget(ConnectionEntry Connection, Guid TableId);

    private sealed class WorkspaceTab
    {
        public required ConnectionEntry Connection { get; set; }
        public required TabItem Item { get; init; }
        public required Control View { get; init; }
        public DatabaseTableRef? NativeTable { get; init; }
        public Guid? ManagedTableId { get; set; }
        public bool Closing { get; set; }
        public Task? CloseTask { get; set; }
    }

    public WorkspaceWindow()
    {
        Title = "SmartSQL";
        Width = 1520;
        Height = 940;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _tree.ItemsSource = _roots;
        _tabs.ItemsSource = _tabItems;
        _tree.DoubleTapped += async (_, e) => { e.Handled = true; await OpenSelectedObjectAsync(); };
        _tree.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await OpenSelectedObjectAsync();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _tabs.SelectionChanged += (_, _) => UpdateActiveContext();
        _mode.IsCheckedChanged += (_, _) =>
        {
            if (!_settingMode && !_starting)
                ApplyMode(_mode.IsChecked == true ? WorkspacePresentationMode.Advanced : WorkspacePresentationMode.Simple, persist: true);
        };
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Control) return;
            if (e.Key == Key.W && ActiveTab is { } tab)
            {
                e.Handled = true;
                await CloseTabAsync(tab);
            }
            else if (e.Key == Key.Tab && _tabItems.Count > 1)
            {
                e.Handled = true;
                _tabs.SelectedIndex = (_tabs.SelectedIndex + 1) % _tabItems.Count;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AutomationProperties.SetName(_tree, "Подключения, схемы и таблицы");
        AutomationProperties.SetName(_tabs, "Открытые таблицы");
        AutomationProperties.SetName(_mode, "Режим интерфейса");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        AddAction(actions, "PostgreSQL…", OpenPostgresAsync, "Подключиться к PostgreSQL");
        AddAction(actions, "SQLite…", OpenNativeSqliteAsync, "Открыть существующую SQLite-базу для просмотра");
        AddAction(actions, "Открыть проект…", OpenManagedAsync, "Открыть проект SmartSQL");
        AddAction(actions, "Новый проект…", CreateManagedAsync, "Создать проект SmartSQL");
        AddAction(actions, "Демонстрация", OpenDemoAsync, "Открыть демонстрационный проект");
        AddAction(actions, "Обновить каталог", RefreshSelectedAsync, "Обновить каталог выбранного подключения");
        AddAction(actions, "Отключить", DisconnectSelectedAsync, "Закрыть выбранное подключение и его вкладки");
        var title = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { UiIcon.Create("UiIconBrand", 30), new TextBlock { Text = "SmartSQL", FontSize = 24, FontWeight = FontWeight.SemiBold } }
        };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        heading.Children.Add(title);
        Grid.SetColumn(_mode, 1);
        heading.Children.Add(_mode);
        var toolbar = new StackPanel { Spacing = 8, Children = { heading, actions } };
        var navigator = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        navigator.Children.Add(new TextBlock { Text = "ПОДКЛЮЧЕНИЯ", Classes = { "navsection" } });
        Grid.SetRow(_tree, 1);
        navigator.Children.Add(_tree);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("250,5,*") };
        body.Children.Add(new Border { Classes = { "sidebar" }, Padding = new Thickness(8), Child = navigator });
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);
        var center = new Grid();
        center.Children.Add(_empty);
        center.Children.Add(_tabs);
        Grid.SetColumn(center, 2);
        body.Children.Add(center);
        var footer = new StackPanel { Spacing = 3, Children = { _status, _context } };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        layout.Children.Add(new Border { Classes = { "toolbar" }, Padding = new Thickness(16, 10), Child = toolbar });
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        var footerBorder = new Border { Classes = { "toolbar" }, Padding = new Thickness(14, 8), Child = footer };
        Grid.SetRow(footerBorder, 2);
        layout.Children.Add(footerBorder);
        Content = layout;
        Opened += async (_, _) => await StartAsync();
        Closing += OnClosing;
        try { OpenManagedView(new MainWindow()); }
        catch (Exception) { _status.Text = "Не удалось открыть проект по умолчанию. Выберите другой файл или подключение."; }
        UpdateActiveContext();
    }

    private WorkspaceTab? ActiveTab => _openTabs.FirstOrDefault(tab => tab.Item == _tabs.SelectedItem);
    private ConnectionEntry? SelectedConnection => _tree.SelectedItem is TreeViewItem node ? node.Tag switch
    {
        ConnectionEntry entry => entry,
        NativeTarget target => target.Connection,
        ManagedTarget target => target.Connection,
        _ => ActiveTab?.Connection
    } : ActiveTab?.Connection;

    private void AddAction(Panel panel, string label, Func<Task> action, string accessibleName)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 4), Classes = { "secondary" } };
        AutomationProperties.SetName(button, accessibleName);
        ToolTip.SetTip(button, accessibleName);
        button.Click += async (_, _) =>
        {
            if (_starting || _shuttingDown) return;
            try { await action(); }
            catch (Exception) { _status.Text = "Не удалось выполнить действие. Проверьте файл, подключение и права доступа."; }
        };
        panel.Children.Add(button);
    }

    private async Task StartAsync()
    {
        try
        {
            _preferences = new JsonWorkspacePreferencesStore(Path.Combine(WorkspacePaths.DataDirectory, "workspace-preferences.json"));
            var saved = await _preferences.LoadModeAsync();
            if (_shuttingDown) return;
            var choice = saved ?? await new PresentationModeDialog().ShowDialog<WorkspacePresentationMode?>(this);
            if (_shuttingDown) return;
            ApplyMode(choice ?? WorkspacePresentationMode.Simple, persist: saved is null && choice is not null);
        }
        catch (Exception)
        {
            ApplyMode(WorkspacePresentationMode.Simple, persist: false);
            _status.Text = "Не удалось прочитать настройки режима. Файл сохранён без изменений; режим можно переключить вверху окна.";
        }
        finally
        {
            _starting = false;
            _mode.IsEnabled = !_shuttingDown;
        }
    }

    private void ApplyMode(WorkspacePresentationMode mode, bool persist)
    {
        if (_shuttingDown) return;
        _settingMode = true;
        try
        {
            _presentationMode = mode;
            _mode.IsChecked = mode == WorkspacePresentationMode.Advanced;
            foreach (var tab in _openTabs)
                if (tab.View is DatabaseTableView native) native.SetPresentationMode(mode);
                else if (tab.View is MainWindow managed) managed.SetPresentationMode(mode);
        }
        finally { _settingMode = false; }
        if (persist && _preferences is not null)
            _preferenceSave = SaveModeAsync(_preferenceSave, mode);
        UpdateActiveContext();
    }

    private async Task SaveModeAsync(Task previous, WorkspacePresentationMode mode)
    {
        await previous;
        try { await _preferences!.SaveModeAsync(mode); }
        catch (Exception)
        {
            if (!_shuttingDown)
                _status.Text = "Режим изменён для текущего сеанса, но сохранить выбор не удалось. Проверьте файл настроек и права доступа.";
        }
    }

    private async Task OpenPostgresAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var store = new JsonConnectionProfileStore(Path.Combine(WorkspacePaths.DataDirectory, "connections.json"));
            var request = await new ConnectionsDialog(store).ShowDialog<ConnectionRequest?>(this);
            if (request is null || _shuttingDown) return;
            var profile = request.Profile;
            var password = request.Password;
            var editing = request.AllowEditing;
            var connection = new WorkspaceConnection(profile.Name, "PostgreSQL",
                $"{profile.Host}:{profile.Port}/{profile.Database} · {profile.Username}", profile.Database, editing,
                async token => editing
                    ? await PostgresDatabaseBrowser.OpenForEditingAsync(profile, password, token)
                    : await PostgresDatabaseBrowser.OpenAsync(profile, password, token));
            await AddNativeConnectionAsync(connection);
        }
        finally { _dialogOpen = false; }
    }

    private async Task OpenNativeSqliteAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Открыть SQLite для просмотра", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("SQLite") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] }, FilePickerFileTypes.All]
            });
            if (files.Count == 0 || _shuttingDown) return;
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException();
            await AddNativeConnectionAsync(new WorkspaceConnection(Path.GetFileName(path), "SQLite", path,
                Path.GetFileName(path), false, async token => await NativeSqliteDatabaseBrowser.OpenAsync(path, token)));
        }
        finally { _dialogOpen = false; }
    }

    private async Task AddNativeConnectionAsync(WorkspaceConnection connection)
    {
        var entry = new ConnectionEntry
        {
            Id = connection.Id, Name = $"{connection.Name} · {++_connectionNumber}", Endpoint = connection.Endpoint,
            Provider = connection.ProviderLabel, Database = connection.DatabaseName, Native = connection,
            Node = new TreeViewItem { IsExpanded = true }
        };
        entry.Node.Tag = entry;
        entry.Node.Header = ConnectionHeader(entry);
        _connections.Add(entry);
        _roots.Add(entry.Node);
        _tree.SelectedItem = entry.Node;
        await RefreshNativeCatalogAsync(entry);
    }

    private Control ConnectionHeader(ConnectionEntry entry)
    {
        var label = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = entry.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{entry.Provider} · {entry.Database}", Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = entry.ManagedPath is not null ? "Проект SmartSQL" : entry.Native?.AllowsEditing == true ? "Редактирование разрешено" : "Только чтение", Classes = { "caption" } }
            }
        };
        ToolTip.SetTip(label, $"{entry.Name}\n{entry.Endpoint}");
        AutomationProperties.SetName(label, $"{entry.Name}, {entry.Provider}, {entry.Endpoint}");
        return label;
    }

    private async Task RefreshNativeCatalogAsync(ConnectionEntry entry)
    {
        if (entry.Native is null || entry.Closing || entry.Refreshing) return;
        entry.Refreshing = true;
        _status.Text = $"Загрузка каталога: {entry.Name}…";
        try
        {
            var catalog = await entry.Native.LoadCatalogAsync();
            if (entry.Closing || _shuttingDown) return;
            entry.Node.ItemsSource = catalog.OrderBy(table => table.Schema, StringComparer.Ordinal)
                .ThenBy(table => table.Name, StringComparer.Ordinal).GroupBy(table => table.Schema, StringComparer.Ordinal)
                .Select(group => new TreeViewItem
                {
                    Header = group.Key, Tag = entry, IsExpanded = true,
                    ItemsSource = group.Select(table => ObjectNode(table.Name, new NativeTarget(entry, table),
                        $"{entry.Endpoint}\n{table.Schema}.{table.Name} · {table.Kind}")).ToArray()
                }).ToArray();
            _status.Text = catalog.Count == 0 ? "Доступных таблиц и представлений не найдено." : $"{entry.Name}: объектов {catalog.Count}. Откройте таблицу из дерева.";
        }
        catch (OperationCanceledException) { if (!entry.Closing && !_shuttingDown) _status.Text = "Загрузка каталога отменена."; }
        catch (Exception)
        {
            if (!entry.Closing && !_shuttingDown)
                _status.Text = $"Не удалось загрузить каталог {entry.Name}. Проверьте подключение и права; затем обновите каталог или подключитесь заново.";
        }
        finally { entry.Refreshing = false; }
    }

    private static TreeViewItem ObjectNode(string name, object target, string detail)
    {
        var node = new TreeViewItem { Header = name, Tag = target };
        ToolTip.SetTip(node, detail);
        AutomationProperties.SetName(node, $"{name}, {detail}");
        return node;
    }

    private async Task OpenSelectedObjectAsync()
    {
        if (_starting || _shuttingDown || _tree.SelectedItem is not TreeViewItem node) return;
        switch (node.Tag)
        {
            case NativeTarget target:
                await OpenNativeTableAsync(target.Connection, target.Table);
                break;
            case ManagedTarget target:
                OpenManagedTable(target.Connection, target.TableId);
                break;
            case ConnectionEntry { ManagedPath: not null } entry when node == entry.Node:
                OpenManagedTable(entry, null);
                break;
        }
    }

    private async Task OpenNativeTableAsync(ConnectionEntry entry, DatabaseTableRef table)
    {
        if (entry.Closing || entry.Native is null) return;
        var existing = _openTabs.FirstOrDefault(tab => tab.Connection == entry && tab.NativeTable == table && !tab.Closing);
        if (existing is not null) { _tabs.SelectedItem = existing.Item; return; }
        var view = new DatabaseTableView(entry.Name, entry.Provider, entry.Endpoint, entry.Database, table, entry.Native.OpenBrowserAsync);
        var tab = AddTab(entry, view, table, null);
        view.SetPresentationMode(_presentationMode);
        await view.InitializeAsync();
        if (!tab.Closing && !_shuttingDown) UpdateActiveContext();
    }

    private ConnectionEntry GetManagedConnection(string path, IReadOnlyList<TableDefinition> tables)
    {
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var entry = _connections.FirstOrDefault(item => item.ManagedPath is not null && string.Equals(item.ManagedPath, fullPath, comparison) && !item.Closing);
        if (entry is null)
        {
            entry = new ConnectionEntry
            {
                Name = $"{Path.GetFileNameWithoutExtension(path)} · {++_connectionNumber}", Endpoint = fullPath,
                Provider = "SQLite", Database = Path.GetFileName(path), ManagedPath = fullPath,
                Node = new TreeViewItem { IsExpanded = true }
            };
            entry.Node.Tag = entry;
            entry.Node.Header = ConnectionHeader(entry);
            _connections.Add(entry);
            _roots.Add(entry.Node);
        }
        entry.ManagedTables = tables;
        entry.Node.ItemsSource = tables.Select(table => ObjectNode(table.Name, new ManagedTarget(entry, table.Id), fullPath)).ToArray();
        return entry;
    }

    private void OpenManagedView(MainWindow view)
    {
        if (view.WorkspacePath is not { } path) return;
        var entry = GetManagedConnection(path, view.WorkspaceTables);
        var tab = AddTab(entry, view, null, view.SelectedTableId);
        view.SetPresentationMode(_presentationMode);
        view.OpenConnectionsRequested += async () => { if (!_shuttingDown) await OpenPostgresAsync(); };
        view.OpenNativeSqliteRequested += async () => { if (!_shuttingDown) await OpenNativeSqliteAsync(); };
        view.WorkspaceOpened += newPath =>
        {
            if (tab.Closing || _shuttingDown) return;
            tab.Connection = GetManagedConnection(newPath, view.WorkspaceTables);
            tab.ManagedTableId = view.SelectedTableId;
            UpdateTabHeader(tab);
            UpdateActiveContext();
        };
        view.TablesChanged += () =>
        {
            if (!tab.Closing && !_shuttingDown && view.WorkspacePath is { } currentPath)
                GetManagedConnection(currentPath, view.WorkspaceTables);
        };
        view.SelectedTableChanged += () =>
        {
            if (tab.Closing || _shuttingDown) return;
            tab.ManagedTableId = view.SelectedTableId;
            UpdateTabHeader(tab);
            UpdateActiveContext();
        };
        view.PresentationModeChanged += mode => { if (!_settingMode && !_starting) ApplyMode(mode, persist: true); };
    }

    private void OpenManagedTable(ConnectionEntry entry, Guid? tableId)
    {
        if (entry.Closing || entry.ManagedPath is null) return;
        var existing = _openTabs.FirstOrDefault(tab => tab.Connection == entry && tab.View is MainWindow && tab.ManagedTableId == tableId && !tab.Closing);
        if (existing is not null) { _tabs.SelectedItem = existing.Item; return; }
        try { OpenManagedView(new MainWindow(entry.ManagedPath, tableId)); }
        catch (Exception) { _status.Text = "Не удалось открыть таблицу проекта. Обновите каталог и проверьте файл."; }
    }

    private WorkspaceTab AddTab(ConnectionEntry entry, Control view, DatabaseTableRef? nativeTable, Guid? managedTableId)
    {
        var item = new TabItem { Content = view, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var tab = new WorkspaceTab { Connection = entry, View = view, Item = item, NativeTable = nativeTable, ManagedTableId = managedTableId };
        _openTabs.Add(tab);
        UpdateTabHeader(tab);
        _tabItems.Add(item);
        _tabs.SelectedItem = item;
        UpdateActiveContext();
        return tab;
    }

    private void UpdateTabHeader(WorkspaceTab tab)
    {
        var tableName = tab.NativeTable is { } table ? $"{table.Schema}.{table.Name}"
            : tab.Connection.ManagedTables.FirstOrDefault(table => table.Id == tab.ManagedTableId)?.Name ?? "Проект";
        var label = new TextBlock
        {
            Text = $"{tableName} · {tab.Connection.Name}", MaxWidth = 270,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        var close = new Button { Content = "×", Classes = { "iconButton" }, Width = 28, Height = 28, MinHeight = 28, Padding = new Thickness(0) };
        ToolTip.SetTip(close, "Закрыть вкладку (Ctrl+W)");
        AutomationProperties.SetName(close, $"Закрыть {tableName}, {tab.Connection.Name}");
        close.Click += async (_, e) => { e.Handled = true; await CloseTabAsync(tab); };
        tab.Item.Header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { label, close } };
        ToolTip.SetTip(tab.Item, $"{tableName}\n{tab.Connection.Name}\n{tab.Connection.Endpoint}");
        AutomationProperties.SetName(tab.Item, $"{tableName}, {tab.Connection.Name}, {tab.Connection.Endpoint}");
    }

    private Task CloseTabAsync(WorkspaceTab tab)
    {
        if (tab.CloseTask is not null) return tab.CloseTask;
        if (tab.View is MainWindow { CanClose: false })
        {
            _status.Text = "Дождитесь завершения импорта или экспорта перед закрытием вкладки.";
            return Task.CompletedTask;
        }
        return tab.CloseTask = CloseTabCoreAsync(tab);
    }

    private async Task CloseTabCoreAsync(WorkspaceTab tab)
    {
        tab.Closing = true;
        tab.Item.IsEnabled = false;
        try
        {
            if (tab.View is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
        catch (Exception) { _status.Text = "При завершении сеанса возникла ошибка. Изменения не повторяются автоматически."; }
        finally
        {
            tab.Item.Content = null;
            _openTabs.Remove(tab);
            _tabItems.Remove(tab.Item);
            UpdateActiveContext();
        }
    }

    private async Task RefreshSelectedAsync()
    {
        var entry = SelectedConnection;
        if (entry is null || entry.Closing) return;
        if (entry.Native is not null) await RefreshNativeCatalogAsync(entry);
        else if (entry.ManagedPath is { } path)
        {
            var workspace = new SqliteWorkspace(path);
            GetManagedConnection(path, workspace.GetTables());
            foreach (var tab in _openTabs.Where(tab => tab.Connection == entry)) UpdateTabHeader(tab);
            _status.Text = "Каталог проекта обновлён.";
        }
    }

    private async Task DisconnectSelectedAsync()
    {
        var entry = SelectedConnection;
        if (entry is null || entry.Closing) return;
        var tabs = _openTabs.Where(tab => tab.Connection == entry).ToArray();
        if (tabs.Any(tab => tab.View is MainWindow { CanClose: false }))
        {
            _status.Text = "Дождитесь завершения импорта или экспорта перед отключением.";
            return;
        }
        entry.Closing = true;
        entry.Native?.Stop();
        await Task.WhenAll(tabs.Select(CloseTabAsync));
        if (entry.Native is not null)
        {
            try { await entry.Native.DisposeAsync(); }
            catch (Exception) { _status.Text = "Подключение закрыто с ошибкой завершения сеанса. Изменения не повторяются автоматически."; }
        }
        _connections.Remove(entry);
        _roots.Remove(entry.Node);
        _status.Text = $"Подключение закрыто: {entry.Name}.";
        UpdateActiveContext();
    }

    private async Task OpenManagedAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Открыть проект SmartSQL", AllowMultiple = false,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(WorkspacePaths.DataDirectory),
                FileTypeFilter = [new FilePickerFileType("Проекты SmartSQL") { Patterns = ["*.db"] }]
            });
            if (files.Count == 0 || _shuttingDown) return;
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException();
            if (!File.Exists(path)) throw new FileNotFoundException();
            OpenManagedView(new MainWindow(path));
        }
        finally { _dialogOpen = false; }
    }

    private async Task CreateManagedAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Создать проект SmartSQL", SuggestedFileName = "Новый проект.db", DefaultExtension = "db",
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(WorkspacePaths.DataDirectory)
            });
            if (file is null || _shuttingDown) return;
            var path = file.TryGetLocalPath() ?? throw new InvalidOperationException();
            if (File.Exists(path)) { _status.Text = "Файл уже существует. Используйте «Открыть проект»."; return; }
            OpenManagedView(new MainWindow(path));
        }
        finally { _dialogOpen = false; }
    }

    private Task OpenDemoAsync()
    {
        var demo = DemoWorkspace.OpenOrCreate();
        var existing = _connections.FirstOrDefault(entry => entry.ManagedPath == demo.Path && !entry.Closing);
        if (existing is not null) OpenManagedTable(existing, demo.MainTableId);
        else OpenManagedView(new MainWindow(demo.Path, demo.MainTableId));
        return Task.CompletedTask;
    }

    private void UpdateActiveContext()
    {
        _empty.IsVisible = _openTabs.Count == 0;
        _tabs.IsVisible = _openTabs.Count > 0;
        var tab = ActiveTab;
        if (tab is null)
        {
            Title = "SmartSQL";
            _context.Text = "Нет активной вкладки · подключения остаются в дереве слева";
            ToolTip.SetTip(_context, null);
            return;
        }
        var entry = tab.Connection;
        var access = entry.ManagedPath is not null ? "Проект SmartSQL" : entry.Native?.AllowsEditing == true ? "Редактирование разрешено; возможности проверяет провайдер" : "Только чтение";
        _context.Text = $"{entry.Name} · {entry.Endpoint} · {access}";
        ToolTip.SetTip(_context, _context.Text);
        Title = $"SmartSQL — {entry.Name}";
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownFinished) return;
        e.Cancel = true;
        if (_shuttingDown) return;
        if (_openTabs.Any(tab => tab.View is MainWindow { CanClose: false }))
        {
            _status.Text = "Дождитесь завершения импорта или экспорта перед выходом.";
            return;
        }
        _shuttingDown = true;
        IsEnabled = false;
        foreach (var entry in _connections) { entry.Closing = true; entry.Native?.Stop(); }
        try
        {
            await Task.WhenAll(_openTabs.ToArray().Select(CloseTabAsync));
            try { await Task.WhenAll(_connections.Where(entry => entry.Native is not null).Select(entry => entry.Native!.DisposeAsync().AsTask())); }
            catch (Exception) { /* Every connection has drained; cleanup errors must not crash the UI thread. */ }
            await _preferenceSave;
        }
        finally
        {
            _shutdownFinished = true;
            Close();
        }
    }
}
