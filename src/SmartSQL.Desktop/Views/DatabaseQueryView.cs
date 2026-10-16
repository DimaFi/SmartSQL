using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SmartSQL.Application;
using SmartSQL.Desktop.Presentation;
using SmartSQL.Desktop.Queries;

namespace SmartSQL.Desktop.Views;

/// <summary>Explicit, read-only SQL execution in an independent connection-owned tab.</summary>
public sealed class DatabaseQueryView : UserControl, IAsyncDisposable
{
    private readonly string _connectionName;
    private readonly string _provider;
    private Func<CancellationToken, Task<IReadOnlySqlExecutor>>? _open;
    private readonly SqlQueryHistoryStore _history;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlySqlExecutor? _executor;
    private readonly SqlTextEditor _sql = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas"), FontSize = 14,
        Text = "SELECT 1 AS result;", MinHeight = 110
    };
    private readonly Button _run = new() { Content = "Выполнить · Ctrl+Enter", Classes = { "primary" } };
    private readonly Button _cancel = new() { Content = "Отмена", IsEnabled = false };
    private readonly Button _load = new() { Content = "Открыть .sql…" };
    private readonly Button _save = new() { Content = "Сохранить .sql…" };
    private readonly Button _historyButton = new() { Content = "История…" };
    private readonly NumericUpDown _limit = new() { Minimum = 1, Maximum = 500, Value = 100, Width = 110, FormatString = "0" };
    private readonly TextBlock _status = new() { Text = "Запрос ещё не выполнялся. SQL-сеанс всегда только для чтения.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _diagnostics = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _mode = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _results = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly HashSet<Window> _dialogs = [];
    private CancellationTokenSource? _operation;
    private TaskCompletionSource? _completion;
    private Task? _disposal;
    private bool _busy;
    private bool _closed;
    private bool _pickingFile;

    public bool CanClose => !_pickingFile;

    public DatabaseQueryView(string connectionName, string provider, string endpoint,
        Func<CancellationToken, Task<IReadOnlySqlExecutor>> open, SqlQueryHistoryStore history)
    {
        _connectionName = connectionName; _provider = provider; _open = open; _history = history;
        _run.Click += async (_, _) => await RunAsync();
        _cancel.Click += (_, _) => { _operation?.Cancel(); _status.Text = "Отмена SQL-запроса…"; };
        _load.Click += async (_, _) => await FileAsync(save: false);
        _save.Click += async (_, _) => await FileAsync(save: true);
        _historyButton.Click += async (_, _) => await ShowHistoryAsync();
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            { e.Handled = true; await RunAsync(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AutomationProperties.SetName(_sql, "Текст SQL-запроса");
        AutomationProperties.SetName(_limit, "Лимит строк результата SQL");
        AutomationProperties.SetName(_results, "Результат SQL только для чтения");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Children =
        {
            _run, _cancel, new TextBlock { Text = "Лимит строк", VerticalAlignment = VerticalAlignment.Center },
            _limit, _load, _save, _historyButton
        } };
        foreach (var control in actions.Children) control.Margin = new Thickness(0, 0, 8, 6);
        var editor = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 6 };
        editor.Children.Add(new TextBlock { Text = $"SQL · {connectionName}", FontSize = 21, FontWeight = FontWeight.SemiBold });
        Grid.SetRow(actions, 1); editor.Children.Add(actions);
        Grid.SetRow(_sql, 2); editor.Children.Add(_sql);
        ToolTip.SetTip(editor, $"{provider} · {endpoint}");
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("220,5,*,Auto,Auto,Auto"), RowSpacing = 8 };
        layout.Children.Add(editor);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(splitter, 1); layout.Children.Add(splitter);
        Grid.SetRow(_results, 2); layout.Children.Add(_results);
        Grid.SetRow(_status, 3); layout.Children.Add(_status);
        var details = new Expander { Header = "Возможности и ограничения SQL", Content = _diagnostics, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(details, 4); layout.Children.Add(details);
        var note = new StackPanel { Spacing = 3, Children =
        {
            _mode, new TextBlock { Text = "∅ означает NULL · пустая строка показана как ⟨пусто⟩ · двойной щелчок — полное значение. История хранит последние 30 успешных запросов локально, включая введённые значения.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }
        } };
        Grid.SetRow(note, 5); layout.Children.Add(note); Content = layout;
        _diagnostics.Text = "Одна команда чтения, максимум 500 строк и 15 секунд. Права редактирования таблиц не распространяются на SQL. Запрос запускается только кнопкой или Ctrl+Enter.";
    }

    public void SetPresentationMode(WorkspacePresentationMode mode) => _mode.Text = mode == WorkspacePresentationMode.Advanced
        ? "Расширенный режим · SQL только для чтения"
        : "Простой режим · открытая SQL-вкладка и её состояние сохранены";

    private void SetErrorStatus() => _status.Classes.Set("error", true);

    private async Task RunAsync()
    {
        if (_busy || _closed) return;
        var request = new SqlReadRequest(_sql.Text ?? string.Empty, (int)(_limit.Value ?? 100));
        try { request.Validate(); }
        catch (ArgumentException exception) { SetErrorStatus(); _status.Text = exception.Message; return; }
        await OperationAsync(async token =>
        {
            _results.ItemsSource = null; _results.Columns.Clear();
            _executor ??= await _open!(token);
            var watch = Stopwatch.StartNew();
            var result = await _executor.ExecuteAsync(request, token);
            if (_closed) return;
            Render(result);
            _status.Text = $"Получено строк: {result.Rows.Count} · {watch.Elapsed.TotalSeconds:F2} с · "
                + (result.Truncated ? $"показаны первые {request.RowLimit}; есть продолжение — уточните запрос или лимит." : "результат полностью загружен.");
            try { await _history.AppendAsync(new(DateTimeOffset.UtcNow, _connectionName, _provider, request.Sql, result.Rows.Count, result.Truncated), token); }
            catch (OperationCanceledException)
            { if (!_closed) _status.Text += " Запрос выполнен; сохранение истории отменено."; }
            catch (Exception)
            { if (!_closed) _status.Text += " Запрос выполнен, но локальную историю сохранить не удалось."; }
        }, "Выполнение SQL только для чтения…");
    }

    private void Render(SqlReadResult result)
    {
        for (var index = 0; index < result.Columns.Count; index++)
        {
            var ordinal = index;
            var column = result.Columns[index];
            var header = new TextBlock { Text = $"{index + 1}. {column.Name}", TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(header, $"Столбец {index + 1}: {column.Name}\nНативный тип: {column.NativeType ?? "не объявлен для выражения"}");
            _results.Columns.Add(new DataGridTemplateColumn
            {
                Header = header, Width = new DataGridLength(190), CanUserSort = false,
                CellTemplate = new FuncDataTemplate<SqlReadRow>((row, _) =>
                {
                    var cell = row?.Cells[ordinal]; var value = cell?.Value;
                    var text = new TextBlock
                    {
                        Text = value is null ? "∅" : value.Length == 0 ? "⟨пусто⟩" : value,
                        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 0),
                        VerticalAlignment = VerticalAlignment.Center, FontStyle = value is null ? FontStyle.Italic : FontStyle.Normal
                    };
                    ToolTip.SetTip(text, $"{column.NativeType ?? "выражение"}{(cell?.StorageClass is { } storage ? " · " + storage : "")}\n{(value is null ? "NULL — отсутствующее значение" : value.Length == 0 ? "Пустая строка" : "Двойной щелчок — полное значение")}");
                    text.DoubleTapped += async (_, e) => { e.Handled = true; await DialogAsync<object?>(new CellValueDialog(column.Name, value)); };
                    return text;
                })
            });
        }
        _results.ItemsSource = result.Rows;
        _diagnostics.Text = string.Join("\n\n", result.Diagnostics.Select(d => d.Message));
    }

    private async Task FileAsync(bool save)
    {
        if (_busy || _closed || TopLevel.GetTopLevel(this) is not { } top) return;
        await OperationAsync(async token =>
        {
            var type = new FilePickerFileType("SQL · UTF-8") { Patterns = ["*.sql"] };
            if (save)
            {
                var file = await PickFileAsync(() => top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                { Title = "Сохранить SQL-запрос", SuggestedFileName = "query.sql", DefaultExtension = "sql", FileTypeChoices = [type] }));
                if (_closed) return;
                if (file is null) { _status.Text = "Сохранение SQL-файла отменено. Запрос не запускался."; return; }
                await SqlQueryFiles.WriteAsync(file.TryGetLocalPath() ?? throw new IOException(), _sql.Text ?? string.Empty, token);
                if (!_closed) _status.Text = "SQL-файл сохранён в UTF-8. Запрос не запускался.";
            }
            else
            {
                var files = await PickFileAsync(() => top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "Открыть SQL-запрос · UTF-8", AllowMultiple = false, FileTypeFilter = [type, FilePickerFileTypes.All] }));
                if (_closed) return;
                if (files.Count == 0) { _status.Text = "Открытие SQL-файла отменено. Запрос не запускался."; return; }
                var text = await SqlQueryFiles.ReadAsync(files[0].TryGetLocalPath() ?? throw new IOException(), token);
                if (!_closed) { _sql.Text = text; _status.Text = "SQL-файл открыт. Проверьте подключение и нажмите «Выполнить»."; }
            }
        }, save ? "Сохранение SQL-файла…" : "Открытие SQL-файла…");
    }

    private async Task<T> PickFileAsync<T>(Func<Task<T>> picker)
    {
        // Native pickers have no cancellation handle. Keep the tab/connection alive
        // until the user finishes that dialog; cancellation still covers subsequent IO.
        _pickingFile = true;
        try
        {
            UpdateControls();
            return await picker();
        }
        finally
        {
            _pickingFile = false;
            if (!_closed) UpdateControls();
        }
    }

    private async Task ShowHistoryAsync()
    {
        if (_busy || _closed) return;
        await OperationAsync(async token =>
        {
            var entries = await _history.ReadAsync(token);
            if (_closed) return;
            var list = new ListBox { ItemsSource = entries, ItemTemplate = new FuncDataTemplate<SqlQueryHistoryEntry>((entry, _) => new TextBlock
            {
                Text = entry is null ? "" : $"{entry.ExecutedUtc.ToLocalTime():g} · {entry.Provider} · {entry.ConnectionName}\n{entry.Sql}",
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 90, TextWrapping = TextWrapping.Wrap
            }) };
            AutomationProperties.SetName(list, "Локальная история успешных SQL-запросов");
            var use = new Button { Content = "Поместить в редактор", Classes = { "primary" }, IsEnabled = false };
            var close = new Button { Content = "Закрыть" };
            var dialog = new Window { Title = "История SQL · только на этом компьютере", Width = 780, Height = 560, MinWidth = 420, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            list.SelectionChanged += (_, _) => use.IsEnabled = list.SelectedItem is SqlQueryHistoryEntry;
            use.Click += (_, _) => { if (list.SelectedItem is SqlQueryHistoryEntry selected) dialog.Close(selected.Sql); };
            close.Click += (_, _) => dialog.Close();
            var body = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
            body.Children.Add(new TextBlock { Text = entries.Count == 0 ? "История пуста. Здесь появятся последние 30 успешных запросов." : "История всех подключений. Выбранный запрос попадёт в редактор текущего подключения и не будет запущен автоматически.", TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(list, 1); body.Children.Add(list);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { close, use } };
            Grid.SetRow(actions, 2); body.Children.Add(actions); dialog.Content = body;
            var selectedSql = await DialogAsync<string?>(dialog);
            if (!_closed && selectedSql is not null) { _sql.Text = selectedSql; _status.Text = "Запрос из истории помещён в редактор. Выполнение требует отдельного запуска."; }
            else if (!_closed) _status.Text = "История закрыта. SQL-запрос не запускался.";
        }, "Чтение локальной истории…");
    }

    private async Task<T?> DialogAsync<T>(Window dialog)
    {
        if (_closed || TopLevel.GetTopLevel(this) is not Window owner) return default;
        _dialogs.Add(dialog);
        try { return await dialog.ShowDialog<T>(owner); }
        finally { _dialogs.Remove(dialog); }
    }

    private async Task OperationAsync(Func<CancellationToken, Task> action, string progress)
    {
        if (_busy || _closed) return;
        _busy = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _completion = completion;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _operation = operation;
        _status.Classes.Set("error", false); _status.Text = progress; UpdateControls();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "Операция отменена. SQL-запрос не повторяется автоматически."; }
        catch (DatabaseBrowserException exception) { if (!_closed) { SetErrorStatus(); _status.Text = exception.Message; } }
        catch (ArgumentException exception) { if (!_closed) { SetErrorStatus(); _status.Text = exception.Message; } }
        catch (Exception) { if (!_closed) { SetErrorStatus(); _status.Text = "Не удалось выполнить операцию. Проверьте подключение, права, UTF-8 и доступ к локальному файлу."; } }
        finally
        {
            _operation = null; _busy = false;
            try { if (!_closed) UpdateControls(); }
            finally { completion.TrySetResult(); }
        }
    }

    private void UpdateControls()
    {
        _run.IsEnabled = _load.IsEnabled = _save.IsEnabled = _historyButton.IsEnabled = !_busy && !_closed;
        _sql.IsReadOnly = _busy || _closed; _limit.IsEnabled = !_busy && !_closed;
        _cancel.IsEnabled = _busy && !_closed && !_pickingFile;
    }

    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        ExceptionDispatchInfo? failure = null;
        void Attempt(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        async Task AttemptAsync(Func<Task> cleanup)
        {
            try { await cleanup(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        _closed = true; _open = null;
        foreach (var dialog in _dialogs.ToArray()) Attempt(dialog.Close);
        await AttemptAsync(_lifetime.CancelAsync);
        // Never dispose the session while its admitted work is still using it.
        if (_completion is { } operation) await AttemptAsync(() => operation.Task);
        if (_executor is not null) await AttemptAsync(() => _executor.DisposeAsync().AsTask());
        _executor = null;
        Attempt(() => _sql.Text = string.Empty);
        Attempt(() => _results.ItemsSource = null);
        Attempt(_results.Columns.Clear);
        Attempt(_lifetime.Dispose);
        failure?.Throw();
    }
}
