using SmartSQL.Application;
using SmartSQL.Desktop.Workspaces;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class WorkspaceSqlConnectionTests
{
    [Fact]
    public async Task SqlAcquisitionsAreIndependentAndBelongToTabsEvenAfterConnectionClose()
    {
        await using var connection = Connection(_ => Task.FromResult<IReadOnlySqlExecutor>(new FakeSql()));
        Assert.True(connection.CanQuery);
        var first = (FakeSql)await connection.OpenSqlAsync();
        var second = (FakeSql)await connection.OpenSqlAsync();
        Assert.NotSame(first, second);
        await connection.DisposeAsync();
        Assert.False(connection.CanQuery);
        Assert.Equal(0, first.Disposals);
        await first.DisposeAsync(); await second.DisposeAsync();
        Assert.Equal(1, first.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenSqlAsync());
    }

    [Fact]
    public async Task ClosingDrainsLateSqlFactoryAndDisposesItsUnclaimedResult()
    {
        var pending = new TaskCompletionSource<IReadOnlySqlExecutor>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sql = new FakeSql();
        var connection = Connection(_ => pending.Task);
        var opening = connection.OpenSqlAsync();
        var disposal = connection.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        pending.SetResult(sql);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, sql.Disposals);
    }

    [Fact]
    public async Task CancelledSqlFactoryResultIsDisposedAndConnectionCanRetry()
    {
        var pending = new TaskCompletionSource<IReadOnlySqlExecutor>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var connection = Connection(_ => ++calls == 1 ? pending.Task : Task.FromResult<IReadOnlySqlExecutor>(new FakeSql()));
        using var cancellation = new CancellationTokenSource();
        var opening = connection.OpenSqlAsync(cancellation.Token);
        cancellation.Cancel();
        var late = new FakeSql(); pending.SetResult(late);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.Equal(1, late.Disposals);
        Assert.True(connection.CanQuery);
        await using var next = await connection.OpenSqlAsync();
        Assert.Equal(2, calls);
    }

    private static WorkspaceConnection Connection(Func<CancellationToken, Task<IReadOnlySqlExecutor>> sql) =>
        new("SQL fixture", "SQLite", "fixture", "fixture", false, _ => throw new NotSupportedException(), sql);
    private sealed class FakeSql : IReadOnlySqlExecutor
    {
        public int Disposals { get; private set; }
        public Task<SqlReadResult> ExecuteAsync(SqlReadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
