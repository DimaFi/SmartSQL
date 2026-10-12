using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using System.Text;
using SmartSQL.Application;
using SmartSQL.Domain;
using SmartSQL.Infrastructure;

namespace SmartSQL.Desktop.Views;

public partial class MainWindow : Window
{
    private const int PageSize = 100;
    private IDataWorkspace? _workspace;
    private TableDefinition? _currentTable;
    private int _offset;
    private string? _search;
    private Guid? _sortFieldId;
    private bool _sortDescending;
    private IReadOnlyList<RecordFilter> _filters = [];
    private bool _updatingSort;
    private bool _updatingSavedFilters;
    private CellChange? _lastChange;
    private bool _transferInProgress;

    private sealed record CellChange(Guid TableId, Guid RecordId, Guid FieldId, string? OldValue);

    public MainWindow()
    {
        InitializeComponent();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartSQL");
        try
        {
            SetWorkspace(Path.Combine(directory, "smartsql.db"));
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось открыть базу по умолчанию: {exception.Message}. Выберите другую базу.";
        }
    }

    private void SetWorkspace(string path)
    {
        var workspace = new SqliteWorkspace(path);
        _workspace = workspace;
        _currentTable = null;
        _offset = 0;
        _search = null;
        _sortFieldId = null;
        _filters = [];
        _lastChange = null;
        SearchBox.Text = string.Empty;
        NaturalQueryBox.Text = string.Empty;
        NaturalQueryBox.IsEnabled = false;
        NaturalQueryButton.IsEnabled = false;
        SortFieldBox.ItemsSource = null;
        UndoButton.IsEnabled = false;
        DatabaseNameText.Text = Path.GetFileNameWithoutExtension(workspace.FilePath);
        DatabasePathText.Text = workspace.FilePath;
        Title = $"SmartSQL — {Path.GetFileName(workspace.FilePath)}";
        CurrentTableText.Text = "Выберите или создайте таблицу";
        TableSummaryText.Text = "Данные хранятся локально";
        StatusText.Text = string.Empty;
        RecordsGrid.IsVisible = false;
        RecordsGrid.ItemsSource = null;
        RecordsGrid.Columns.Clear();
        EmptyStateText.IsVisible = false;
        WelcomePanel.IsVisible = true;
        AddButton.IsEnabled = false;
        FilterButton.IsEnabled = false;
        ClearFiltersButton.IsEnabled = false;
        FilterButton.Content = "Фильтры";
        SavedFilterBox.ItemsSource = null;
        SaveFilterButton.IsEnabled = false;
        DeleteSavedFilterButton.IsEnabled = false;
        AddFieldButton.IsEnabled = false;
        EditSchemaButton.IsEnabled = false;
        DeleteFieldButton.IsEnabled = false;
        DeleteTableButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        ImportJsonButton.IsEnabled = false;
        ExportJsonButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        PreviousButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        RefreshTables();
    }

    private void RefreshTables() => TablesList.ItemsSource = _workspace?.GetTables().ToArray();

    private bool BeginTransfer()
    {
        if (_transferInProgress)
            return false;
        _transferInProgress = true;
        WorkspaceContent.IsEnabled = false;
        return true;
    }

    private void EndTransfer()
    {
        _transferInProgress = false;
        WorkspaceContent.IsEnabled = true;
    }

    private void OnOpenDemo(object? sender, RoutedEventArgs e)
    {
        try
        {
            var demo = DemoWorkspace.OpenOrCreate();
            SetWorkspace(demo.Path);
            TablesList.SelectedItem = ((IEnumerable<TableDefinition>)TablesList.ItemsSource!)
                .First(table => table.Id == demo.MainTableId);
            StatusText.Text = "Демонстрационная база открыта. Вы можете редактировать записи и применять сохранённые фильтры.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось открыть демонстрацию: {exception.Message}";
        }
    }

    private async void OnCreateDatabase(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Создать базу SmartSQL",
                SuggestedFileName = "Новая база.db",
                DefaultExtension = "db"
            });
            if (file is null)
                return;
            var path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный путь к файлу базы.");
            if (File.Exists(path))
                throw new IOException("Файл уже существует. Чтобы открыть его, используйте «Открыть базу».");
            SetWorkspace(path);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось создать базу: {exception.Message}";
        }
    }

    private async void OnOpenDatabase(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Открыть базу SmartSQL",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Базы SmartSQL") { Patterns = ["*.db"] }]
            });
            if (files.Count == 0)
                return;
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный путь к файлу базы.");
            if (!File.Exists(path))
                throw new FileNotFoundException("Файл базы не найден.", path);
            SetWorkspace(path);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось открыть базу: {exception.Message}";
        }
    }

    private async void OnBackupDatabase(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null)
            return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Создать резервную копию",
                SuggestedFileName = $"{Path.GetFileNameWithoutExtension(_workspace.FilePath)}-backup.db",
                DefaultExtension = "db"
            });
            if (file is null)
                return;
            var path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный путь для копии.");
            _workspace.CreateBackup(path);
            StatusText.Text = $"Резервная копия сохранена: {path}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось создать копию: {exception.Message}";
        }
    }

    private async void OnRestoreBackup(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null)
            return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выбрать резервную копию SmartSQL",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Базы SmartSQL") { Patterns = ["*.db"] }]
            });
            if (files.Count == 0)
                return;
            var sourcePath = files[0].TryGetLocalPath()
                ?? throw new InvalidOperationException("Нужен локальный файл резервной копии.");
            var currentPath = _workspace.FilePath;
            var result = await new RestoreBackupDialog(_workspace, sourcePath).ShowDialog<RestorationResult?>(this);
            if (result is null)
                return;
            SetWorkspace(currentPath);
            StatusText.Text = $"База восстановлена. Предыдущее состояние сохранено: {result.SafetyBackupPath}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось восстановить базу: {exception.Message}";
        }
    }

    private async void OnCreateTable(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null)
            return;
        try
        {
            var table = await new CreateTableDialog(_workspace).ShowDialog<TableDefinition?>(this);
            if (table is null)
                return;
            RefreshTables();
            TablesList.SelectedItem = ((IEnumerable<TableDefinition>)TablesList.ItemsSource!).First(item => item.Id == table.Id);
            StatusText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось создать таблицу: {exception.Message}";
        }
    }

    private async void OnCreateTableFromCsv(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null)
            return;
        if (!BeginTransfer())
            return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Создать таблицу из CSV",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });
            if (files.Count == 0)
                return;
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный CSV-файл.");
            if (new FileInfo(path).Length > 50_000_000)
                throw new IOException("CSV-файл слишком большой для одного импорта (предел 50 МБ).");
            StatusText.Text = "Проверка CSV…";
            var content = await Task.Run(() =>
            {
                using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
                return reader.ReadToEnd();
            });
            var source = await Task.Run(() => CsvTransfer.InspectNewTable(content));
            var preview = await new CsvNewTableDialog(content, source, Path.GetFileNameWithoutExtension(path))
                .ShowDialog<CsvNewTablePreview?>(this);
            if (preview is null)
            {
                StatusText.Text = "Создание таблицы отменено. Данные не изменены.";
                return;
            }
            var workspace = _workspace;
            StatusText.Text = "Создание таблицы и импорт CSV…";
            var table = await Task.Run(() => preview.Commit(workspace));
            RefreshTables();
            TablesList.SelectedItem = ((IEnumerable<TableDefinition>)TablesList.ItemsSource!)
                .First(item => item.Id == table.Id);
            StatusText.Text = $"Создана таблица «{table.Name}». Импортировано записей: {preview.RowCount}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось создать таблицу из CSV: {exception.Message}";
        }
        finally
        {
            EndTransfer();
        }
    }

    private async void OnAddField(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        try
        {
            var field = await new AddFieldDialog(_workspace, _currentTable.Id).ShowDialog<FieldDefinition?>(this);
            if (field is null)
                return;
            var tableId = _currentTable.Id;
            RefreshTables();
            TablesList.SelectedItem = ((IEnumerable<TableDefinition>)TablesList.ItemsSource!).First(item => item.Id == tableId);
            StatusText.Text = $"Поле «{field.Name}» добавлено.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось добавить поле: {exception.Message}";
        }
    }

    private async void OnEditSchema(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        try
        {
            var updated = await new EditSchemaDialog(_workspace, _currentTable).ShowDialog<TableDefinition?>(this);
            if (updated is null)
                return;
            RefreshTables();
            TablesList.SelectedItem = ((IEnumerable<TableDefinition>)TablesList.ItemsSource!).First(item => item.Id == updated.Id);
            StatusText.Text = "Названия таблицы и полей обновлены.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось изменить названия: {exception.Message}";
        }
    }

    private async void OnDeleteField(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        var tableId = _currentTable.Id;
        var result = await new DeleteSchemaDialog(_workspace, _currentTable, deleteTable: false)
            .ShowDialog<DestructiveChangeResult?>(this);
        if (result is null)
            return;
        RefreshTables();
        TablesList.SelectedItem = ((IEnumerable<TableDefinition>)TablesList.ItemsSource!).First(item => item.Id == tableId);
        StatusText.Text = $"Поле удалено. Резервная копия: {result.BackupPath}";
    }

    private async void OnDeleteTable(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        var result = await new DeleteSchemaDialog(_workspace, _currentTable, deleteTable: true)
            .ShowDialog<DestructiveChangeResult?>(this);
        if (result is null)
            return;
        RefreshTables();
        TablesList.SelectedItem = null;
        StatusText.Text = $"Таблица удалена. Резервная копия: {result.BackupPath}";
    }

    private void OnTableSelected(object? sender, SelectionChangedEventArgs e)
    {
        _currentTable = TablesList.SelectedItem as TableDefinition;
        _offset = 0;
        _search = null;
        _sortFieldId = null;
        _filters = [];
        _lastChange = null;
        SearchBox.Text = string.Empty;
        NaturalQueryBox.Text = string.Empty;
        UndoButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        AddButton.IsEnabled = _currentTable is not null;
        NaturalQueryBox.IsEnabled = _currentTable is not null;
        NaturalQueryButton.IsEnabled = _currentTable is not null;
        FilterButton.IsEnabled = _currentTable is not null;
        ClearFiltersButton.IsEnabled = false;
        FilterButton.Content = "Фильтры";
        SaveFilterButton.IsEnabled = false;
        DeleteSavedFilterButton.IsEnabled = false;
        AddFieldButton.IsEnabled = _currentTable is not null;
        EditSchemaButton.IsEnabled = _currentTable is not null;
        DeleteFieldButton.IsEnabled = _currentTable is not null && _currentTable.Fields.Count > 1;
        DeleteTableButton.IsEnabled = _currentTable is not null;
        ImportButton.IsEnabled = _currentTable is not null;
        ExportButton.IsEnabled = _currentTable is not null;
        ImportJsonButton.IsEnabled = _currentTable is not null;
        ExportJsonButton.IsEnabled = _currentTable is not null;
        RecordsGrid.Columns.Clear();
        if (_currentTable is null)
        {
            CurrentTableText.Text = "Выберите или создайте таблицу";
            TableSummaryText.Text = "Данные хранятся локально";
            RecordsGrid.ItemsSource = null;
            RecordsGrid.IsVisible = false;
            EmptyStateText.IsVisible = false;
            WelcomePanel.IsVisible = true;
            return;
        }
        WelcomePanel.IsVisible = false;
        _updatingSort = true;
        SortFieldBox.ItemsSource = new[] { "Без сортировки" }.Concat(_currentTable.Fields.Select(field => field.Name)).ToArray();
        SortFieldBox.SelectedIndex = 0;
        DescendingBox.IsChecked = false;
        _updatingSort = false;
        RefreshSavedFilters();
        CurrentTableText.Text = _currentTable.Name;
        for (var index = 0; index < _currentTable.Fields.Count; index++)
        {
            RecordsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = $"{_currentTable.Fields[index].Name} · {TypeLabel(_currentTable.Fields[index].Type)}",
                Binding = new Binding($"Cells[{index}].Value") { Mode = BindingMode.TwoWay },
                Width = new DataGridLength(180)
            });
        }
        RefreshRecords();
    }

    private void RefreshRecords()
    {
        if (_workspace is null || _currentTable is null)
            return;
        try
        {
            var tableId = _currentTable.Id;
            var fields = _currentTable.Fields;
            var records = _workspace.QueryRecords(tableId, _offset, PageSize, _search, _sortFieldId, _sortDescending, _filters);
            RecordsGrid.ItemsSource = records.Select(record =>
                new EditableRow(record.Id, fields.Select(field =>
                    new EditableCell(ForDisplay(field.Type, record.Values.GetValueOrDefault(field.Id)),
                        (oldValue, value) =>
                        {
                            _workspace.UpdateCell(tableId, record.Id, field.Id, value);
                            _lastChange = new CellChange(tableId, record.Id, field.Id, oldValue);
                            UndoButton.IsEnabled = true;
                            StatusText.Text = string.Empty;
                        },
                        message => StatusText.Text = message)).ToArray())).ToArray();
            var count = _workspace.CountRecords(tableId, _search, _filters);
            TableSummaryText.Text = $"{count} записей{(string.IsNullOrWhiteSpace(_search) && _filters.Count == 0 ? "" : " найдено")} · страница {_offset / PageSize + 1}";
            PreviousButton.IsEnabled = _offset > 0;
            NextButton.IsEnabled = _offset + PageSize < count;
            RecordsGrid.IsVisible = count > 0;
            EmptyStateText.IsVisible = count == 0;
            WelcomePanel.IsVisible = false;
            EmptyStateText.Text = string.IsNullOrWhiteSpace(_search) && _filters.Count == 0
                ? "Таблица пуста. Нажмите «+ Новая запись»."
                : "По вашему запросу ничего не найдено.";
            DeleteButton.IsEnabled = false;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось загрузить записи: {exception.Message}";
        }
    }

    private void OnAddRecord(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        try
        {
            _workspace.AddRecord(_currentTable.Id, new Dictionary<Guid, string?>());
            _offset = 0;
            _search = null;
            _filters = [];
            SearchBox.Text = string.Empty;
            FilterButton.Content = "Фильтры";
            ClearFiltersButton.IsEnabled = false;
            SaveFilterButton.IsEnabled = false;
            _updatingSavedFilters = true;
            SavedFilterBox.SelectedItem = null;
            DeleteSavedFilterButton.IsEnabled = false;
            _updatingSavedFilters = false;
            RefreshRecords();
            StatusText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось добавить запись: {exception.Message}";
        }
    }

    private async void OnImportCsv(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        if (!BeginTransfer())
            return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импортировать CSV в выбранную таблицу",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });
            if (files.Count == 0)
                return;
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный CSV-файл.");
            if (new FileInfo(path).Length > 50_000_000)
                throw new IOException("CSV-файл слишком большой для одного импорта (предел 50 МБ).");
            var workspace = _workspace;
            var table = _currentTable;
            StatusText.Text = "Проверка CSV…";
            var content = await Task.Run(() =>
            {
                using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
                return reader.ReadToEnd();
            });
            var source = await Task.Run(() => CsvTransfer.Inspect(table, content));
            var preview = await new CsvMappingDialog(table, content, source).ShowDialog<CsvImportPreview?>(this);
            if (preview is null)
            {
                StatusText.Text = "Импорт отменён. Данные не изменены.";
                return;
            }
            if (!await new CsvPreviewDialog(preview).ShowDialog<bool>(this))
            {
                StatusText.Text = "Импорт отменён. Данные не изменены.";
                return;
            }
            StatusText.Text = "Импорт CSV…";
            var count = await Task.Run(() => preview.Commit(workspace));
            _offset = 0;
            RefreshRecords();
            StatusText.Text = $"Импортировано записей: {count}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось импортировать CSV: {exception.Message}";
        }
        finally
        {
            EndTransfer();
        }
    }

    private async void OnExportCsv(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        if (!BeginTransfer())
            return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Экспортировать CSV для Excel",
                SuggestedFileName = $"{_currentTable.Name}.csv",
                DefaultExtension = "csv"
            });
            if (file is null)
                return;
            var path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный путь для CSV.");
            var workspace = _workspace;
            var tableId = _currentTable.Id;
            StatusText.Text = "Экспорт CSV…";
            var count = await Task.Run(() =>
            {
                var exported = 0;
                AtomicFileExport.Create(path, stream =>
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
                    exported = CsvTransfer.Export(workspace, tableId, writer, spreadsheetSafe: true);
                });
                return exported;
            });
            StatusText.Text = $"Экспортировано записей: {count}. Формулы обезврежены префиксом '.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось экспортировать CSV: {exception.Message}";
        }
        finally
        {
            EndTransfer();
        }
    }

    private async void OnImportJson(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        if (!BeginTransfer())
            return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импортировать JSON в выбранную таблицу",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON SmartSQL") { Patterns = ["*.json"] }]
            });
            if (files.Count == 0)
                return;
            var path = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный JSON-файл.");
            if (new FileInfo(path).Length > 50_000_000)
                throw new IOException("JSON-файл слишком большой для одного импорта (предел 50 МБ).");
            var workspace = _workspace;
            var tableId = _currentTable.Id;
            StatusText.Text = "Проверка JSON…";
            var content = await Task.Run(() =>
            {
                using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
                return reader.ReadToEnd();
            });
            var preview = await Task.Run(() => JsonTableTransfer.PrepareImport(workspace, tableId, content));
            if (!await new JsonPreviewDialog(preview, _currentTable).ShowDialog<bool>(this))
            {
                StatusText.Text = "Импорт отменён. Данные не изменены.";
                return;
            }
            StatusText.Text = "Импорт JSON…";
            var count = await Task.Run(() => preview.Commit(workspace));
            _offset = 0;
            RefreshRecords();
            StatusText.Text = $"Импортировано записей: {count}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось импортировать JSON: {exception.Message}";
        }
        finally
        {
            EndTransfer();
        }
    }

    private async void OnExportJson(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null)
            return;
        if (!BeginTransfer())
            return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Экспортировать таблицу в JSON",
                SuggestedFileName = $"{_currentTable.Name}.json",
                DefaultExtension = "json"
            });
            if (file is null)
                return;
            var path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Нужен локальный путь для JSON.");
            var workspace = _workspace;
            var tableId = _currentTable.Id;
            StatusText.Text = "Экспорт JSON…";
            var count = await Task.Run(() =>
            {
                var exported = 0;
                AtomicFileExport.Create(path, stream => exported = JsonTableTransfer.Export(workspace, tableId, stream));
                return exported;
            });
            StatusText.Text = $"Экспортировано записей: {count}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось экспортировать JSON: {exception.Message}";
        }
        finally
        {
            EndTransfer();
        }
    }

    private void OnRecordSelected(object? sender, SelectionChangedEventArgs e) =>
        DeleteButton.IsEnabled = RecordsGrid.SelectedItem is EditableRow;

    private async void OnDeleteRecord(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null || RecordsGrid.SelectedItem is not EditableRow row)
            return;
        if (!await ConfirmDeleteAsync())
            return;
        try
        {
            _workspace.DeleteRecords(_currentTable.Id, [row.Id]);
            if (_lastChange?.RecordId == row.Id)
            {
                _lastChange = null;
                UndoButton.IsEnabled = false;
            }
            if (_offset > 0 && _workspace.CountRecords(_currentTable.Id, _search, _filters) <= _offset)
                _offset -= PageSize;
            RefreshRecords();
            StatusText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось удалить запись: {exception.Message}";
        }
    }

    private Task<bool> ConfirmDeleteAsync() =>
        ConfirmAsync("Удаление записи", "Удалить выбранную запись? Это действие нельзя отменить.");

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new Button { Content = "Отмена" };
        var delete = new Button { Content = "Удалить" };
        cancel.Click += (_, _) => dialog.Close(false);
        delete.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, delete } }
            }
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private void OnPreviousPage(object? sender, RoutedEventArgs e)
    {
        _offset = Math.Max(0, _offset - PageSize);
        RefreshRecords();
    }

    private void OnNextPage(object? sender, RoutedEventArgs e)
    {
        _offset += PageSize;
        RefreshRecords();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplySearch();
    }

    private void OnNaturalQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplyNaturalQuery();
    }

    private void OnNaturalQuery(object? sender, RoutedEventArgs e) => ApplyNaturalQuery();

    private void ApplyNaturalQuery()
    {
        if (_currentTable is null)
            return;
        try
        {
            var interpreted = NaturalLanguageQuery.Interpret(_currentTable, NaturalQueryBox.Text ?? string.Empty);
            _filters = interpreted.Filters;
            _search = null;
            SearchBox.Text = string.Empty;
            _offset = 0;
            FilterButton.Content = $"Фильтры: {_filters.Count}";
            ClearFiltersButton.IsEnabled = true;
            SaveFilterButton.IsEnabled = true;
            _updatingSavedFilters = true;
            SavedFilterBox.SelectedItem = null;
            DeleteSavedFilterButton.IsEnabled = false;
            _updatingSavedFilters = false;
            RefreshRecords();
            StatusText.Text = $"Распознано: {interpreted.Description}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось разобрать запрос: {exception.Message}";
        }
    }

    private void OnSearch(object? sender, RoutedEventArgs e) => ApplySearch();

    private void ApplySearch()
    {
        _search = SearchBox.Text?.Trim();
        _offset = 0;
        RefreshRecords();
    }

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        ApplySearch();
    }

    private async void OnEditFilters(object? sender, RoutedEventArgs e)
    {
        if (_currentTable is null)
            return;
        try
        {
            var filters = await new FilterDialog(_currentTable, _filters).ShowDialog<IReadOnlyList<RecordFilter>?>(this);
            if (filters is null)
                return;
            _filters = filters;
            _offset = 0;
            FilterButton.Content = _filters.Count == 0 ? "Фильтры" : $"Фильтры: {_filters.Count}";
            ClearFiltersButton.IsEnabled = _filters.Count > 0;
            SaveFilterButton.IsEnabled = _filters.Count > 0;
            _updatingSavedFilters = true;
            SavedFilterBox.SelectedItem = null;
            DeleteSavedFilterButton.IsEnabled = false;
            _updatingSavedFilters = false;
            RefreshRecords();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось применить фильтры: {exception.Message}";
        }
    }

    private void OnClearFilters(object? sender, RoutedEventArgs e)
    {
        _filters = [];
        _offset = 0;
        FilterButton.Content = "Фильтры";
        ClearFiltersButton.IsEnabled = false;
        SaveFilterButton.IsEnabled = false;
        _updatingSavedFilters = true;
        SavedFilterBox.SelectedItem = null;
        _updatingSavedFilters = false;
        RefreshRecords();
    }

    private void RefreshSavedFilters(Guid? selectId = null)
    {
        if (_workspace is null || _currentTable is null)
            return;
        _updatingSavedFilters = true;
        var saved = _workspace.GetSavedFilters(_currentTable.Id).ToArray();
        SavedFilterBox.ItemsSource = saved;
        SavedFilterBox.SelectedItem = selectId.HasValue ? saved.FirstOrDefault(item => item.Id == selectId) : null;
        DeleteSavedFilterButton.IsEnabled = SavedFilterBox.SelectedItem is SavedFilterDefinition;
        _updatingSavedFilters = false;
    }

    private void OnSavedFilterSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingSavedFilters || SavedFilterBox.SelectedItem is not SavedFilterDefinition saved)
        {
            DeleteSavedFilterButton.IsEnabled = false;
            return;
        }
        _filters = saved.Filters.ToArray();
        _offset = 0;
        FilterButton.Content = $"Фильтры: {_filters.Count}";
        ClearFiltersButton.IsEnabled = true;
        SaveFilterButton.IsEnabled = true;
        DeleteSavedFilterButton.IsEnabled = true;
        RefreshRecords();
        StatusText.Text = $"Применён фильтр «{saved.Name}».";
    }

    private async void OnSaveFilter(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null || _filters.Count == 0)
            return;
        try
        {
            var suggested = (SavedFilterBox.SelectedItem as SavedFilterDefinition)?.Name;
            var name = await new SaveFilterDialog(suggested).ShowDialog<string?>(this);
            if (name is null)
                return;
            var saved = _workspace.SaveFilter(_currentTable.Id, name, _filters);
            RefreshSavedFilters(saved.Id);
            StatusText.Text = $"Фильтр «{saved.Name}» сохранён.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось сохранить фильтр: {exception.Message}";
        }
    }

    private async void OnDeleteSavedFilter(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _currentTable is null || SavedFilterBox.SelectedItem is not SavedFilterDefinition saved)
            return;
        if (!await ConfirmAsync("Удаление фильтра", $"Удалить сохранённый фильтр «{saved.Name}»? Данные таблицы не изменятся."))
            return;
        try
        {
            _workspace.DeleteSavedFilter(_currentTable.Id, saved.Id);
            RefreshSavedFilters();
            StatusText.Text = $"Фильтр «{saved.Name}» удалён. Текущие условия остались применены.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось удалить фильтр: {exception.Message}";
        }
    }

    private void OnSortChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingSort || _currentTable is null)
            return;
        var index = SortFieldBox.SelectedIndex;
        _sortFieldId = index > 0 && index <= _currentTable.Fields.Count
            ? _currentTable.Fields[index - 1].Id : null;
        _sortDescending = DescendingBox.IsChecked == true;
        _offset = 0;
        RefreshRecords();
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || _lastChange is null)
            return;
        try
        {
            _workspace.UpdateCell(_lastChange.TableId, _lastChange.RecordId, _lastChange.FieldId, _lastChange.OldValue);
            _lastChange = null;
            UndoButton.IsEnabled = false;
            RefreshRecords();
            StatusText.Text = "Последнее изменение ячейки отменено.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось отменить изменение: {exception.Message}";
        }
    }

    private static string TypeLabel(FieldType type) => type switch
    {
        FieldType.Text => "Текст",
        FieldType.Number => "Число",
        FieldType.Date => "Дата",
        FieldType.Boolean => "Да/Нет",
        _ => "?"
    };

    private static string? ForDisplay(FieldType type, string? value) => type == FieldType.Boolean
        ? value switch { "1" => "Да", "0" => "Нет", _ => value }
        : value;
}
