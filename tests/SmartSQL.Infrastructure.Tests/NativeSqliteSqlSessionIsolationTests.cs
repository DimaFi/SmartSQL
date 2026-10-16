using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class NativeSqliteSqlSessionIsolationTests
{
    private static readonly TimeSpan CompletionBound = TimeSpan.FromSeconds(8);
    private const string LongQuery = "WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i+1 FROM n WHERE i<1000000000) SELECT sum(i) FROM n";

    static NativeSqliteSqlSessionIsolationTests() => SQLitePCL.Batteries_V2.Init();

    [Fact]
    public async Task ConcurrentSessionsKeepTheirOwnSqlColumnsLimitsAndExactResults()
    {
        using var file = new Fixture();
        var hash = file.Hash();
        await using IReadOnlySqlExecutor first = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        await using IReadOnlySqlExecutor second = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        Assert.NotSame(first, second);

        var firstQuery = first.ExecuteAsync(new("SELECT id AS first_id,note,exact,payload FROM items ORDER BY id", 2));
        var secondQuery = second.ExecuteAsync(new("SELECT id AS second_id,note FROM items WHERE id>=10 ORDER BY id DESC", 1));
        await Task.WhenAll(firstQuery, secondQuery).WaitAsync(CompletionBound);
        var firstResult = await firstQuery;
        var secondResult = await secondQuery;

        Assert.Equal("first_id", firstResult.Columns[0].Name);
        Assert.Equal("second_id", secondResult.Columns[0].Name);
        Assert.Equal(4, firstResult.Columns.Count);
        Assert.Equal(2, secondResult.Columns.Count);
        Assert.Equal(new[] { "2", "10" }, firstResult.Rows.Select(row => row.Cells[0].Value));
        Assert.Equal("40", Assert.Single(secondResult.Rows).Cells[0].Value);
        Assert.True(firstResult.Truncated);
        Assert.True(secondResult.Truncated);
        Assert.Equal(new[] { "9223372036854775807", "-9223372036854775808" }, firstResult.Rows.Select(row => row.Cells[2].Value));
        Assert.All(firstResult.Rows, row => Assert.Equal("integer", row.Cells[2].StorageClass));
        Assert.Equal("0x00FF80", firstResult.Rows[0].Cells[3].Value);
        Assert.Equal("blob", firstResult.Rows[0].Cells[3].StorageClass);
        Assert.Equal("Привет 🌍", firstResult.Rows[0].Cells[1].Value);
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task CancellingOneAdmittedQueryDoesNotCancelOtherSessionOrPoisonEitherSession()
    {
        using var file = new Fixture();
        var hash = file.Hash();
        await using IReadOnlySqlExecutor first = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        await using IReadOnlySqlExecutor second = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        using var cancellation = new CancellationTokenSource();

        var cancelled = first.ExecuteAsync(new(LongQuery), cancellation.Token);
        var independent = second.ExecuteAsync(new("SELECT exact,note FROM items WHERE id=2", 1));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(CompletionBound));
        var result = await independent.WaitAsync(CompletionBound);
        var row = Assert.Single(result.Rows);
        Assert.Equal("9223372036854775807", row.Cells[0].Value);
        Assert.Equal("Привет 🌍", row.Cells[1].Value);
        Assert.False(result.Truncated);

        var recovered = first.ExecuteAsync(new("SELECT count(*) FROM items", 1));
        var stillIndependent = second.ExecuteAsync(new("SELECT exact FROM items WHERE id=10", 1));
        await Task.WhenAll(recovered, stillIndependent).WaitAsync(CompletionBound);
        Assert.Equal("4", Assert.Single((await recovered).Rows).Cells[0].Value);
        Assert.Equal("-9223372036854775808", Assert.Single((await stillIndependent).Rows).Cells[0].Value);
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task DisposingOneSessionDrainsItsRunningAndQueuedQueriesWhileOtherSessionRemainsUsable()
    {
        using var file = new Fixture();
        var hash = file.Hash();
        await using IReadOnlySqlExecutor first = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        await using IReadOnlySqlExecutor second = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);

        // ExecuteAsync admits the first call synchronously before returning its pending task;
        // the second call waits on that session's serialization gate. No sleep/race hook needed.
        var running = first.ExecuteAsync(new(LongQuery));
        var queued = first.ExecuteAsync(new("SELECT 999 AS should_not_run", 1));
        var independent = second.ExecuteAsync(new("SELECT id,note FROM items ORDER BY id DESC", 3));
        var disposal = first.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(CompletionBound));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(CompletionBound));
        await disposal.WaitAsync(CompletionBound);
        await first.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.ExecuteAsync(new("SELECT 1", 1)));

        var concurrentResult = await independent.WaitAsync(CompletionBound);
        Assert.Equal(new[] { "40", "30", "10" }, concurrentResult.Rows.Select(row => row.Cells[0].Value));
        Assert.True(concurrentResult.Truncated);
        var afterClose = await second.ExecuteAsync(new("SELECT id,exact,note,payload FROM items WHERE id=2", 1)).WaitAsync(CompletionBound);
        var row = Assert.Single(afterClose.Rows);
        Assert.Equal(new[] { "2", "9223372036854775807", "Привет 🌍", "0x00FF80" }, row.Cells.Select(cell => cell.Value));
        Assert.Equal(new[] { "integer", "integer", "text", "blob" }, row.Cells.Select(cell => cell.StorageClass));
        Assert.False(afterClose.Truncated);
        Assert.Equal(hash, file.Hash());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(TestPaths.Root, "native-sql-session-isolation", Guid.NewGuid().ToString("N"));
        public string Path { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "ordinary.db");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE items(id INTEGER PRIMARY KEY,note TEXT,exact INTEGER,payload BLOB); " +
                "INSERT INTO items VALUES(2,'Привет 🌍',9223372036854775807,x'00ff80'),(10,'',-9223372036854775808,x''),(30,NULL,30,NULL),(40,'  ',40,x'01'); PRAGMA user_version=73";
            command.ExecuteNonQuery();
        }

        public string Hash()
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
