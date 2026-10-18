using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using SmartSQL.Application;
using SmartSQL.Desktop.Presentation;
using SmartSQL.Desktop.Queries;
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
    private readonly Button _newSql = new() { Content = "SQL…", IsVisible = false };
    private readonly SqlQueryHistoryStore _sqlHistory = new(Path.Combine(WorkspacePaths.DataDirectory, "queries", "history.json"));
    private readonly StackPanel _empty = new()
    {
        Spacing = 14, MaxWidth = 610, Margin = new Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private IWorkspacePreferencesStore? _preferences;
    private WorkspacePresentationMode _presentationMode;
    private Task _preferenceSave = Task.CompletedTask;
    private bool _settingMode;
    private bool _starting = true;
    private bool _dialogOpen;
    private Window? _activeDialog;
    private bool _shuttingDown;
    private bool _shutdownFinished;
    private int _connectionNumber;
    private int _sqlNumber;
    private readonly string? _themeStartupWarning;

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
        public int SqlNumber { get; init; }
        public bool Closing { get; set; }
        public Task? CloseTask { get; set; }
        public bool CloseFailed { get; set; }
    }

    public WorkspaceWindow(string? themeStartupWarning = null)
    {
        _themeStartupWarning = themeStartupWarning;
        Title = "SmartSQL";
        // Keep native caption actions explicit, independent of the workspace layout.
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = false;
        CanResize = true;
        CanMinimize = true;
        CanMaximize = true;
        ShowInTaskbar = true;
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
        AutomationProperties.SetName(_tree, "Источники данных: локальные проекты, подключения, схемы и таблицы");
        AutomationProperties.SetName(_tabs, "Открытые таблицы");
        AutomationProperties.SetName(_mode, "Режим интерфейса");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        AddAction(actions, "Подключить PostgreSQL…", OpenPostgresAsync, "Подключиться к серверу PostgreSQL");
        AddAction(actions, "Открыть SQLite-файл…", OpenNativeSqliteAsync, "Открыть существующую SQLite-базу без изменения её формата");
        _newSql.Click += (_, _) =>
        {
            try { OpenSqlTab(); }
            catch (Exception) { _status.Text = "Не удалось открыть SQL-вкладку. Проверьте выбранное подключение."; }
        };
        AutomationProperties.SetName(_newSql, "Открыть SQL-вкладку выбранного подключения только для чтения");
        ToolTip.SetTip(_newSql, "SQL только для чтения для подключения, выбранного в дереве или активной вкладке");
        actions.Children.Add(_newSql);
        AddAction(actions, "Открыть проект…", OpenManagedAsync, "Открыть существующий проект SmartSQL");
        AddAction(actions, "Создать проект…", CreateManagedAsync, "Создать новую локальную базу SmartSQL");
        AddAction(actions, "Демонстрация", OpenDemoAsync, "Открыть демонстрационный проект");
        AddAction(actions, "Обновить каталог", RefreshSelectedAsync, "Обновить каталог выбранного подключения");
        AddAction(actions, "Отключить", DisconnectSelectedAsync, "Закрыть выбранное подключение и его вкладки");
        var brandIcon = UiIcon.Create("UiIconBrand", 30);
        brandIcon.Bind(PathIcon.ForegroundProperty, new DynamicResourceExtension("UiBrandBrush"));
        var title = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { brandIcon, new TextBlock { Text = "SmartSQL", FontSize = 24, FontWeight = FontWeight.SemiBold } }
        };
        var currentTheme = Avalonia.Application.Current?.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light
            ? ApplicationTheme.Light : ApplicationTheme.Gray;
        var theme = new Button
        {
            Content = currentTheme == ApplicationTheme.Gray ? "Тема: серая" : "Тема: светлая", MinWidth = 148
        };
        void UpdateThemeCaption()
        {
            theme.Content = currentTheme == ApplicationTheme.Gray ? "Тема: серая" : "Тема: светлая";
            var action = currentTheme == ApplicationTheme.Gray ? "Включить светлую тему" : "Включить серую тему";
            AutomationProperties.SetName(theme, action);
            ToolTip.SetTip(theme, action + ". Выбор сохраняется для следующего запуска.");
        }
        UpdateThemeCaption();
        theme.Click += (_, _) =>
        {
            if (Avalonia.Application.Current is not { } application) return;
            currentTheme = currentTheme == ApplicationTheme.Gray ? ApplicationTheme.Light : ApplicationTheme.Gray;
            ThemePreferences.Apply(application, currentTheme);
            UpdateThemeCaption();
            try { ThemePreferences.CreateDefault().Save(currentTheme); }
            catch (Exception)
            {
                _status.Text = "Тема изменена для текущего сеанса, но сохранить выбор не удалось. Проверьте файл настроек и права доступа.";
            }
        };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        heading.Children.Add(title);
        Grid.SetColumn(theme, 1);
        heading.Children.Add(theme);
        Grid.SetColumn(_mode, 2);
        heading.Children.Add(_mode);
        var toolbar = new StackPanel { Spacing = 8, Children = { heading, actions } };
        var navigator = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        navigator.Children.Add(new TextBlock { Text = "ИСТОЧНИКИ ДАННЫХ", Classes = { "navsection" } });
        Grid.SetRow(_tree, 1);
        navigator.Children.Add(_tree);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("250,5,*") };
        body.Children.Add(new Border { Classes = { "sidebar" }, Padding = new Thickness(8), Child = navigator });
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);
        BuildEmptyWorkspace();
        var center = new Grid();
        center.Children.Add(_empty);
        center.Children.Add(_tabs);
        Grid.SetColumn(center, 2);
        body.Children.Add(center);
        var storageLocation = new TextBox
        {
            Text = "Хранилище SmartSQL: " + WorkspacePaths.DataDirectory,
            IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            FontSize = 12, MaxHeight = 38
        };
        AutomationProperties.SetName(storageLocation, "Папка данных и настроек SmartSQL");
        ToolTip.SetTip(storageLocation, "Папка настроек и локальных данных. Открытые внешние проекты остаются по выбранным вами путям.");
        var footer = new StackPanel { Spacing = 3, Children = { _status, _context, storageLocation } };
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

    /// <summary>Empty workspace actions reuse the existing open/create paths; no new DB is created by this view.</summary>
    private void BuildEmptyWorkspace()
    {
        _empty.Children.Add(new TextBlock
        {
            Text = "С чего начнём?",
            Classes = { "pageTitle" },
            TextAlignment = TextAlignment.Center
        });
        _empty.Children.Add(new TextBlock
        {
            Text = "Создайте локальный проект, откройте прежнюю базу или подключитесь к PostgreSQL. Если слева уже есть источник, откройте его таблицу двойным щелчком или клавишей Enter.",
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Classes = { "muted" }
        });
        var entryActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        AddAction(entryActions, "Создать локальный проект…", CreateManagedAsync, "Создать новый локальный проект SmartSQL");
        AddAction(entryActions, "Открыть проект…", OpenManagedAsync, "Открыть существующий проект SmartSQL");
        AddAction(entryActions, "Открыть SQLite-файл…", OpenNativeSqliteAsync, "Открыть существующую SQLite-базу");
        AddAction(entryActions, "Подключить PostgreSQL…", OpenPostgresAsync, "Подключить сервер PostgreSQL");
        if (entryActions.Children.FirstOrDefault() is Button primary)
        {
            primary.Classes.Remove("secondary");
            primary.Classes.Add("primary");
        }
        _empty.Children.Add(entryActions);
        _empty.Children.Add(new TextBlock
        {
            Text = "Локальный проект работает без сервера. PostgreSQL требует доступ к работающему серверу. Открытие существующей базы не преобразует её в проект SmartSQL.",
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Classes = { "caption" }
        });
        AutomationProperties.SetName(_empty, "Начало работы с источниками данных");
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
            var choice = saved ?? await ShowWorkspaceDialogAsync<WorkspacePresentationMode?>(new PresentationModeDialog());
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
            if (_themeStartupWarning is not null)
                _status.Text = string.IsNullOrEmpty(_status.Text) ? _themeStartupWarning : _status.Text + " " + _themeStartupWarning;
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
            _newSql.IsVisible = mode == WorkspacePresentationMode.Advanced;
            foreach (var tab in _openTabs)
                if (tab.View is DatabaseTableView native) native.SetPresentationMode(mode);
                else if (tab.View is MainWindow managed) managed.SetPresentationMode(mode);
                else if (tab.View is DatabaseQueryView query) query.SetPresentationMode(mode);
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
            var request = await ShowWorkspaceDialogAsync<ConnectionRequest?>(new ConnectionsDialog(store));
            if (request is null || _shuttingDown) return;
            var profile = request.Profile;
            var password = request.Password;
            var editing = request.AllowEditing;
            var connection = new WorkspaceConnection(profile.Name, "PostgreSQL",
                $"{profile.Host}:{profile.Port}/{profile.Database} · {profile.Username}", profile.Database, editing,
                async token => editing
                    ? await PostgresDatabaseBrowser.OpenForEditingAsync(profile, password, token)
                    : await PostgresDatabaseBrowser.OpenAsync(profile, password, token),
                async token => await PostgresReadOnlySqlExecutor.OpenAsync(profile, password, token));
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
                Title = "Открыть существующую SQLite-базу", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("SQLite") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] }, FilePickerFileTypes.All]
            });
            if (files.Count == 0 || _shuttingDown) return;
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException();
            var backupDirectory = Path.Combine(WorkspacePaths.DataDirectory, "backups", "native-sqlite");
            var choice = await ShowWorkspaceDialogAsync<NativeSqliteOpenChoice?>(
                new NativeSqliteOpenDialog(path, backupDirectory));
            if (choice is null || _shuttingDown) return;
            var editing = choice.AllowEditing;
            await AddNativeConnectionAsync(new WorkspaceConnection(Path.GetFileName(path), "SQLite", path,
                Path.GetFileName(path), editing, async token =>
                {
                    if (editing)
                        return await NativeSqliteDatabaseBrowser.OpenEditableAsync(path, backupDirectory, token);
                    return await NativeSqliteDatabaseBrowser.OpenAsync(path, token);
                }, async token => await NativeSqliteReadOnlySqlExecutor.OpenAsync(path, token)));
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
                new TextBlock { Text = WorkspaceSourceLabels.Kind(entry.ManagedPath is not null, entry.Provider), Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = WorkspaceSourceLabels.Access(entry.ManagedPath is not null, entry.Native?.AllowsEditing == true), Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis }
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

    private void OpenSqlTab()
    {
        if (_starting || _shuttingDown || _presentationMode != WorkspacePresentationMode.Advanced) return;
        var entry = SelectedConnection;
        if (entry is null || entry.Closing || entry.Native is not { CanQuery: true } connection)
        {
            _status.Text = "Выберите обычное SQLite- или PostgreSQL-подключение в дереве. Для управляемого проекта SQL пока недоступен.";
            return;
        }
        var view = new DatabaseQueryView(entry.Name, entry.Provider, entry.Endpoint, connection.OpenSqlAsync, _sqlHistory);
        view.SetPresentationMode(_presentationMode);
        AddTab(entry, view, null, null);
        _status.Text = "SQL-вкладка открыта. Запрос запускается явно, сеанс всегда только для чтения.";
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
        var tab = new WorkspaceTab { Connection = entry, View = view, Item = item, NativeTable = nativeTable, ManagedTableId = managedTableId, SqlNumber = view is DatabaseQueryView ? ++_sqlNumber : 0 };
        _openTabs.Add(tab);
        UpdateTabHeader(tab);
        _tabItems.Add(item);
        _tabs.SelectedItem = item;
        UpdateActiveContext();
        return tab;
    }

    private void UpdateTabHeader(WorkspaceTab tab)
    {
        var tableName = tab.View is DatabaseQueryView ? $"SQL {tab.SqlNumber}" : tab.NativeTable is { } table ? $"{table.Schema}.{table.Name}"
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
        if (tab.View is DatabaseQueryView { CanClose: false })
        {
            _status.Text = "Завершите или отмените выбор SQL-файла перед закрытием вкладки.";
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
        catch (Exception)
        {
            tab.CloseFailed = true;
            _status.Text = "При завершении сеанса возникла ошибка. Изменения не повторяются автоматически.";
        }
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
            var workspace = SqliteWorkspace.OpenExisting(path);
            GetManagedConnection(path, workspace.GetTables());
            foreach (var tab in _openTabs.Where(tab => tab.Connection == entry)) UpdateTabHeader(tab);
            _status.Text = (MainWindow.ManagedUpgradeMessage(workspace) + " Каталог проекта обновлён.").Trim();
        }
    }

    private async Task DisconnectSelectedAsync()
    {
        var entry = SelectedConnection;
        if (entry is null || entry.Closing) return;
        var tabs = _openTabs.Where(tab => tab.Connection == entry).ToArray();
        if (tabs.Any(tab => tab.View is DatabaseQueryView { CanClose: false }))
        {
            _status.Text = "Завершите или отмените выбор SQL-файла перед отключением подключения.";
            return;
        }
        if (tabs.Any(tab => tab.View is MainWindow { CanClose: false }))
        {
            _status.Text = "Дождитесь завершения импорта или экспорта перед отключением.";
            return;
        }
        entry.Closing = true;
        entry.Native?.Stop();
        await Task.WhenAll(tabs.Select(CloseTabAsync));
        var cleanupFailed = tabs.Any(tab => tab.CloseFailed);
        if (entry.Native is not null)
        {
            try { await entry.Native.DisposeAsync(); }
            catch (Exception) { cleanupFailed = true; }
        }
        _connections.Remove(entry);
        _roots.Remove(entry.Node);
        _status.Text = cleanupFailed
            ? $"Подключение закрыто: {entry.Name}. При завершении сеанса возникла ошибка. Изменения не повторяются автоматически."
            : $"Подключение закрыто: {entry.Name}.";
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
            var path = files[0].TryGetLocalPath()
                ?? throw new InvalidOperationException("Выберите локальный файл проекта SmartSQL.");
            OpenManagedView(new MainWindow(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status.Text = $"Не удалось открыть проект: {exception.Message}";
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
            var path = file.TryGetLocalPath()
                ?? throw new InvalidOperationException("Выберите локальный путь нового проекта SmartSQL.");
            if (File.Exists(path)) { _status.Text = "Файл уже существует. Используйте «Открыть проект»."; return; }
            OpenManagedView(new MainWindow(SqliteWorkspace.CreateNew(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status.Text = $"Не удалось создать проект: {exception.Message}";
        }
        finally { _dialogOpen = false; }
    }

    private Task OpenDemoAsync()
    {
        var demo = DemoWorkspace.OpenOrCreate();
        var existing = _connections.FirstOrDefault(entry => entry.ManagedPath == demo.Path && !entry.Closing);
        if (existing is not null) OpenManagedTable(existing, demo.MainTableId);
        else OpenManagedView(new MainWindow(demo.Workspace, demo.MainTableId));
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
        var access = tab.View is DatabaseQueryView ? "SQL всегда только для чтения" : entry.ManagedPath is not null ? "Проект SmartSQL" : entry.Native?.AllowsEditing == true ? "Редактирование разрешено; возможности проверяет провайдер" : "Только чтение";
        _context.Text = $"{entry.Name} · {entry.Endpoint} · {access}";
        ToolTip.SetTip(_context, _context.Text);
        Title = $"SmartSQL — {entry.Name}";
    }

    private async Task<T?> ShowWorkspaceDialogAsync<T>(Window dialog)
    {
        if (_shuttingDown)
            return default;
        _activeDialog = dialog;
        try { return await dialog.ShowDialog<T>(this); }
        finally
        {
            if (ReferenceEquals(_activeDialog, dialog))
                _activeDialog = null;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownFinished) return;
        e.Cancel = true;
        if (_shuttingDown) return;
        if (_openTabs.Any(tab => tab.View is DatabaseQueryView { CanClose: false }))
        {
            _status.Text = "Завершите или отмените выбор SQL-файла перед выходом из приложения.";
            return;
        }
        if (_openTabs.Any(tab => tab.View is MainWindow { CanClose: false }))
        {
            _status.Text = "Дождитесь завершения импорта или экспорта перед выходом.";
            return;
        }
        _shuttingDown = true;
        IsEnabled = false;
        _activeDialog?.Close();
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
