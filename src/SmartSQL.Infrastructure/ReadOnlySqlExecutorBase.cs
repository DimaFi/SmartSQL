using System.Runtime.ExceptionServices;
using SmartSQL.Application;

namespace SmartSQL.Infrastructure;

/// <summary>Serializes query work and drains every admitted operation before releasing session resources.</summary>
public abstract class ReadOnlySqlExecutorBase : IReadOnlySqlExecutor
{
    private readonly object _state = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource _drained = Completed();
    private Task? _disposal;
    private bool _closing;
    private int _operations;

    public async Task<SqlReadResult> ExecuteAsync(SqlReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        CancellationTokenSource linked;
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            cancellationToken.ThrowIfCancellationRequested();
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            linked.CancelAfter(SqlReadLimits.ExecutionTimeout);
            if (_operations++ == 0)
                _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var entered = false;
        try
        {
            await _serial.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            linked.Token.ThrowIfCancellationRequested();
            var result = await ExecuteCoreAsync(request, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception)
        {
            bool closing;
            lock (_state) closing = _closing;
            if (cancellationToken.IsCancellationRequested || closing)
                throw new OperationCanceledException(cancellationToken);
            if (linked.IsCancellationRequested)
                throw new DatabaseBrowserException("query_timed_out", "SQL-запрос превысил предел 15 секунд. Уменьшите объём работы.");
            if (exception is DatabaseBrowserException or ObjectDisposedException)
                throw;
            throw Sanitize(exception);
        }
        finally
        {
            if (entered) _serial.Release();
            linked.Dispose();
            lock (_state)
                if (--_operations == 0) _drained.TrySetResult();
        }
    }

    protected abstract Task<SqlReadResult> ExecuteCoreAsync(SqlReadRequest request, CancellationToken cancellationToken);
    protected virtual DatabaseBrowserException Sanitize(Exception exception) =>
        new("query_failed", "Не удалось выполнить SQL-запрос только для чтения. Проверьте синтаксис, поддерживаемые операции и доступ к данным.");
    protected virtual ValueTask ReleaseResourcesAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        lock (_state)
        {
            if (_disposal is not null) return new(_disposal);
            _closing = true;
            _disposal = CompleteDisposeAsync(_drained.Task);
            return new(_disposal);
        }
    }

    private async Task CompleteDisposeAsync(Task drained)
    {
        ExceptionDispatchInfo? failure = null;
        async Task AttemptAsync(Func<Task> cleanup)
        {
            try { await cleanup().ConfigureAwait(false); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }

        try
        {
            await AttemptAsync(_lifetime.CancelAsync).ConfigureAwait(false);
            await AttemptAsync(() => drained).ConfigureAwait(false);
            await AttemptAsync(() => ReleaseResourcesAsync().AsTask()).ConfigureAwait(false);
        }
        finally
        {
            try { _lifetime.Dispose(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        failure?.Throw();
    }

    private static TaskCompletionSource Completed()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
