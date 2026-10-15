using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Workspaces;

/// <summary>Owns acquisitions and catalog work; returned table sessions belong to their callers.</summary>
public sealed class WorkspaceConnection : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Func<CancellationToken, Task<IDatabaseBrowser>>? _openAsync;
    private Func<CancellationToken, Task<IReadOnlySqlExecutor>>? _openSqlAsync;
    private TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _cancellationTask;
    private Task? _disposeTask;
    private int _operations;
    private bool _closing;

    public WorkspaceConnection(string name, string providerLabel, string endpoint, string databaseName,
        bool allowsEditing, Func<CancellationToken, Task<IDatabaseBrowser>> openAsync,
        Func<CancellationToken, Task<IReadOnlySqlExecutor>>? openSqlAsync = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerLabel);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(openAsync);
        Name = name;
        ProviderLabel = providerLabel;
        Endpoint = endpoint;
        DatabaseName = databaseName;
        AllowsEditing = allowsEditing;
        _openAsync = openAsync;
        _openSqlAsync = openSqlAsync;
        _drained.SetResult();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public string ProviderLabel { get; }
    public string Endpoint { get; }
    public string DatabaseName { get; }
    public bool AllowsEditing { get; }
    public bool CanQuery { get { lock (_gate) return !_closing && _openSqlAsync is not null; } }
    public bool IsClosing { get { lock (_gate) return _closing; } }

    public async Task<IDatabaseBrowser> OpenBrowserAsync(CancellationToken cancellationToken = default)
    {
        var (factory, linked) = BeginOperation(cancellationToken);
        try
        {
            return await AcquireAsync(factory, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            linked.Dispose();
            EndOperation();
        }
    }

    public async Task<IReadOnlySqlExecutor> OpenSqlAsync(CancellationToken cancellationToken = default)
    {
        Func<CancellationToken, Task<IReadOnlySqlExecutor>> factory;
        CancellationTokenSource linked;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            cancellationToken.ThrowIfCancellationRequested();
            factory = _openSqlAsync ?? throw new NotSupportedException("Для этого подключения SQL-вкладки пока недоступны.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            if (_operations++ == 0)
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            var executor = await factory(linked.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Провайдер не вернул SQL-сеанс.");
            lock (_gate)
            {
                if (!_closing && !linked.IsCancellationRequested)
                    return executor;
            }
            await executor.DisposeAsync().ConfigureAwait(false);
            throw new OperationCanceledException(linked.Token);
        }
        finally
        {
            linked.Dispose();
            EndOperation();
        }
    }

    public async Task<IReadOnlyList<DatabaseTableRef>> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        var (factory, linked) = BeginOperation(cancellationToken);
        try
        {
            await using var browser = await AcquireAsync(factory, linked.Token).ConfigureAwait(false);
            var tables = await browser.GetTablesAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return tables;
        }
        finally
        {
            linked.Dispose();
            EndOperation();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_closing)
                return;
            _closing = true;
            _openAsync = null;
            _openSqlAsync = null;
            // Request cancellation now; await callbacks before disposing the source.
            _cancellationTask = _lifetime.CancelAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            Stop();
            _disposeTask ??= CompleteDisposeAsync(_cancellationTask!, _drained.Task);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task CompleteDisposeAsync(Task cancellation, Task drained)
    {
        try
        {
            await Task.WhenAll(cancellation, drained).ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private (Func<CancellationToken, Task<IDatabaseBrowser>> Factory, CancellationTokenSource Linked)
        BeginOperation(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            cancellationToken.ThrowIfCancellationRequested();
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            if (_operations++ == 0)
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return (_openAsync!, linked);
        }
    }

    private async Task<IDatabaseBrowser> AcquireAsync(Func<CancellationToken, Task<IDatabaseBrowser>> factory,
        CancellationToken cancellationToken)
    {
        var browser = await factory(cancellationToken).ConfigureAwait(false);
        if (browser is null)
            throw new InvalidOperationException("Провайдер не вернул сеанс подключения.");
        lock (_gate)
        {
            if (!_closing && !cancellationToken.IsCancellationRequested)
                return browser;
        }
        await browser.DisposeAsync().ConfigureAwait(false);
        throw new OperationCanceledException(cancellationToken);
    }

    private void EndOperation()
    {
        lock (_gate)
        {
            if (--_operations == 0)
                _drained.TrySetResult();
        }
    }
}
