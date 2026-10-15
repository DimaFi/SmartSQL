using SmartSQL.Application;
using SmartSQL.Desktop.Workspaces;
using SmartSQL.Domain;

namespace SmartSQL.Desktop.Tests;

public sealed class WorkspaceConnectionTests
{
    [Xunit.Fact]
    public async Task IdenticalProfilesHaveDistinctIdentityAndIndependentSessions()
    {
        var sessions = new List<FakeBrowser>();
        Task<IDatabaseBrowser> Open(CancellationToken _)
        {
            var browser = new FakeBrowser();
            sessions.Add(browser);
            return Task.FromResult<IDatabaseBrowser>(browser);
        }
        await using var first = Connection(Open);
        await using var second = Connection(Open);
        Xunit.Assert.NotEqual(first.Id, second.Id);
        Xunit.Assert.Equal(first.Name, second.Name);
        Xunit.Assert.Equal("PostgreSQL", first.ProviderLabel);
        Xunit.Assert.Equal("localhost:5432", first.Endpoint);
        Xunit.Assert.Equal("database", first.DatabaseName);
        Xunit.Assert.False(first.AllowsEditing);
        var firstTab = await first.OpenBrowserAsync();
        var secondTab = await first.OpenBrowserAsync();
        var otherConnection = await second.OpenBrowserAsync();
        Xunit.Assert.NotSame(firstTab, secondTab);
        Xunit.Assert.NotSame(firstTab, otherConnection);
        await first.DisposeAsync();
        Xunit.Assert.All(sessions, browser => Xunit.Assert.Equal(0, browser.DisposeCount));
        await firstTab.DisposeAsync();
        Xunit.Assert.Equal(0, sessions[1].DisposeCount);
        await secondTab.DisposeAsync();
        await otherConnection.DisposeAsync();
        Xunit.Assert.All(sessions, browser => Xunit.Assert.Equal(1, browser.DisposeCount));
    }

    [Xunit.Fact]
    public async Task CloseDuringConnectDisposesLateResultAndDrainsConcurrentDisposals()
    {
        var completion = Pending<IDatabaseBrowser>();
        var browser = new FakeBrowser();
        var calls = 0;
        var connection = Connection(_ => { calls++; return completion.Task; });
        var open = connection.OpenBrowserAsync();
        connection.Stop();
        connection.Stop();
        Xunit.Assert.True(connection.IsClosing);
        var dispose = connection.DisposeAsync().AsTask();
        var concurrentDispose = connection.DisposeAsync().AsTask();
        Xunit.Assert.Same(dispose, concurrentDispose);
        Xunit.Assert.False(dispose.IsCompleted);
        await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenBrowserAsync());
        await Xunit.Assert.ThrowsAsync<ObjectDisposedException>(() => connection.LoadCatalogAsync());
        completion.SetResult(browser);
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => open);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Xunit.Assert.Equal(1, browser.DisposeCount);
        Xunit.Assert.Equal(1, calls);
        await connection.DisposeAsync();
        connection.Stop();
    }

    [Xunit.Fact]
    public async Task CallerCancellationDisposesLateResultWithoutClosingConnection()
    {
        var completion = Pending<IDatabaseBrowser>();
        var late = new FakeBrowser();
        var calls = 0;
        await using var connection = Connection(_ => ++calls == 1
            ? completion.Task : Task.FromResult<IDatabaseBrowser>(new FakeBrowser()));
        using var cancellation = new CancellationTokenSource();
        var open = connection.OpenBrowserAsync(cancellation.Token);
        cancellation.Cancel();
        completion.SetResult(late);
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => open);
        Xunit.Assert.Equal(1, late.DisposeCount);
        Xunit.Assert.False(connection.IsClosing);
        await using var next = await connection.OpenBrowserAsync();
        Xunit.Assert.Equal(2, calls);
    }

    [Xunit.Fact]
    public async Task AlreadyCancelledOperationNeverInvokesFactory()
    {
        var calls = 0;
        await using var connection = Connection(_ => { calls++; return Task.FromResult<IDatabaseBrowser>(new FakeBrowser()); });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.OpenBrowserAsync(cancellation.Token));
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.LoadCatalogAsync(cancellation.Token));
        Xunit.Assert.Equal(0, calls);
    }

    [Xunit.Fact]
    public async Task CatalogUsesItsOwnTemporarySessionAndDisposesItAfterSuccess()
    {
        DatabaseTableRef[] tables = [new("public", "items", DatabaseObjectKind.Table)];
        var browser = new FakeBrowser { Tables = _ => Task.FromResult<IReadOnlyList<DatabaseTableRef>>(tables) };
        await using var connection = Connection(_ => Task.FromResult<IDatabaseBrowser>(browser));
        var result = await connection.LoadCatalogAsync();
        Xunit.Assert.Equal(tables, result);
        Xunit.Assert.Equal(1, browser.DisposeCount);
    }

    [Xunit.Fact]
    public async Task CatalogFailureDisposesSessionAndAllowsRefresh()
    {
        var browser = new FakeBrowser
        {
            Tables = _ => Task.FromException<IReadOnlyList<DatabaseTableRef>>(new InvalidOperationException("Metadata failed."))
        };
        var calls = 0;
        await using var connection = Connection(_ => Task.FromResult<IDatabaseBrowser>(++calls == 1 ? browser : new FakeBrowser()));
        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoadCatalogAsync());
        Xunit.Assert.Equal(1, browser.DisposeCount);
        Xunit.Assert.False(connection.IsClosing);
        Xunit.Assert.Empty(await connection.LoadCatalogAsync());
    }

    [Xunit.Fact]
    public async Task DisposalCancelsMetadataButWaitsForItBeforeDisposingProvider()
    {
        var started = Pending<bool>();
        var cancelled = Pending<bool>();
        var completion = Pending<IReadOnlyList<DatabaseTableRef>>();
        var browser = new FakeBrowser
        {
            Tables = async token =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult(true));
                started.SetResult(true);
                return await completion.Task; // Simulate a provider that finishes despite cancellation.
            }
        };
        var connection = Connection(_ => Task.FromResult<IDatabaseBrowser>(browser));
        var catalog = connection.LoadCatalogAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dispose = connection.DisposeAsync().AsTask();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Xunit.Assert.False(dispose.IsCompleted);
        Xunit.Assert.Equal(0, browser.DisposeCount);
        completion.SetResult(Array.Empty<DatabaseTableRef>());
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Xunit.Assert.Equal(1, browser.DisposeCount);
    }

    [Xunit.Fact]
    public async Task CallerCancellationRemainsLinkedThroughoutMetadataRead()
    {
        var started = Pending<bool>();
        var cancelled = Pending<bool>();
        var browser = new FakeBrowser
        {
            Tables = async token =>
            {
                started.SetResult(true);
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Observe cancellation at the provider await. A separately registered callback
                    // can legitimately be unregistered by this continuation before it is invoked.
                    cancelled.TrySetResult(true);
                    throw;
                }
                return Array.Empty<DatabaseTableRef>();
            }
        };
        await using var connection = Connection(_ => Task.FromResult<IDatabaseBrowser>(browser));
        using var cancellation = new CancellationTokenSource();
        var catalog = connection.LoadCatalogAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog);
        Xunit.Assert.Equal(1, browser.DisposeCount);
        Xunit.Assert.False(connection.IsClosing);
    }

    [Xunit.Fact]
    public async Task FailedAcquisitionDrainsAndCanBeRetried()
    {
        var calls = 0;
        await using var connection = Connection(_ => ++calls == 1
            ? Task.FromException<IDatabaseBrowser>(new InvalidOperationException("Connection failed."))
            : Task.FromResult<IDatabaseBrowser>(new FakeBrowser()));
        await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenBrowserAsync());
        await using var browser = await connection.OpenBrowserAsync();
        Xunit.Assert.Equal(2, calls);
        Xunit.Assert.False(connection.IsClosing);
    }

    private static WorkspaceConnection Connection(Func<CancellationToken, Task<IDatabaseBrowser>> open) =>
        new("Same profile", "PostgreSQL", "localhost:5432", "database", false, open);

    private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeBrowser : IDatabaseBrowser
    {
        public int DisposeCount { get; private set; }
        public Func<CancellationToken, Task<IReadOnlyList<DatabaseTableRef>>> Tables { get; init; } =
            _ => Task.FromResult<IReadOnlyList<DatabaseTableRef>>(Array.Empty<DatabaseTableRef>());

        public Task<IReadOnlyList<DatabaseTableRef>> GetTablesAsync(CancellationToken cancellationToken = default) => Tables(cancellationToken);
        public Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(DatabaseTableRef table, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
