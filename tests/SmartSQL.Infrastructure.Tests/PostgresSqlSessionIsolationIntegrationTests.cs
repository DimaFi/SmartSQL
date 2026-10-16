using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>Read-only independent SQL sessions against the explicitly enabled dedicated PostgreSQL fixture.</summary>
public sealed class PostgresSqlSessionIsolationIntegrationTests
{
    private const string ExactNumeric = "1234567890123456789012345678901234567890.12345678901234567890";
    private static readonly TimeSpan CompletionLimit = TimeSpan.FromSeconds(8);

    [DedicatedPostgresFact]
    public async Task ConcurrentSessionsKeepDifferentQueriesRowLimitsAndExactResultsIndependent()
    {
        await using var first = await Open();
        await using var second = await Open();
        Assert.NotSame(first, second);

        var firstQuery = first.ExecuteAsync(new SqlReadRequest(
            "SELECT i AS first_session, pg_sleep(0.02) AS pause FROM generate_series(1,5) AS n(i) ORDER BY i", 2));
        var secondQuery = second.ExecuteAsync(new SqlReadRequest(
            $"SELECT i AS second_session, {ExactNumeric}::numeric AS amount FROM generate_series(10,12) AS n(i) ORDER BY i", 4));
        await Task.WhenAll(firstQuery, secondQuery).WaitAsync(CompletionLimit);

        var firstResult = await firstQuery;
        var secondResult = await secondQuery;
        Assert.Equal("first_session", firstResult.Columns[0].Name);
        Assert.Equal(new[] { "1", "2" }, firstResult.Rows.Select(row => row.Cells[0].Value));
        Assert.True(firstResult.Truncated);
        Assert.Equal("second_session", secondResult.Columns[0].Name);
        Assert.Equal(new[] { "10", "11", "12" }, secondResult.Rows.Select(row => row.Cells[0].Value));
        Assert.All(secondResult.Rows, row => Assert.Equal(ExactNumeric, row.Cells[1].Value));
        Assert.False(secondResult.Truncated);

        var firstAgain = await first.ExecuteAsync(new SqlReadRequest("VALUES(101),(102)", 1));
        Assert.Equal("101", Assert.Single(firstAgain.Rows).Cells[0].Value);
        Assert.True(firstAgain.Truncated);
        Assert.Equal(new[] { "10", "11", "12" }, secondResult.Rows.Select(row => row.Cells[0].Value));
    }

    [DedicatedPostgresFact]
    public async Task CancellingAndDisposingOneSessionDrainsItsAdmittedQueriesWithoutAffectingAnother()
    {
        await using var first = await Open();
        await using var second = await Open();
        using var cancellation = new CancellationTokenSource();
        var running = first.ExecuteAsync(new SqlReadRequest("SELECT pg_sleep(10)"), cancellation.Token);
        // ExecuteAsync admits work before returning; pg_sleep keeps it pending if server execution is reached.
        Assert.False(running.IsCompleted);
        var queued = first.ExecuteAsync(new SqlReadRequest("SELECT pg_sleep(10)"));
        Assert.False(queued.IsCompleted);

        var independent = second.ExecuteAsync(new SqlReadRequest(
            $"SELECT {ExactNumeric}::numeric AS exact, NULL::text AS absent, ''::text AS empty, pg_sleep(0.2) AS pause", 1));
        cancellation.Cancel();
        var disposal = first.DisposeAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(CompletionLimit));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(CompletionLimit));
        await disposal.WaitAsync(CompletionLimit);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.ExecuteAsync(new SqlReadRequest("SELECT 3")));

        var survived = Assert.Single((await independent.WaitAsync(CompletionLimit)).Rows);
        Assert.Equal(ExactNumeric, survived.Cells[0].Value);
        Assert.Null(survived.Cells[1].Value);
        Assert.Equal(string.Empty, survived.Cells[2].Value);
        var afterClose = await second.ExecuteAsync(new SqlReadRequest(
            "SELECT 9223372036854775807::bigint, decode('00ff80','hex'), current_setting('transaction_read_only')", 1));
        Assert.Equal(new[] { "9223372036854775807", "\\x00ff80", "on" }, Assert.Single(afterClose.Rows).Cells.Select(cell => cell.Value));
        Assert.False(afterClose.Truncated);
    }

    private static Task<PostgresReadOnlySqlExecutor> Open()
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432")
            throw new InvalidOperationException("Only the dedicated loopback PostgreSQL fixture is permitted.");
        return PostgresReadOnlySqlExecutor.OpenAsync(new ConnectionProfile(Guid.NewGuid(), "SQL isolation fixture", DatabaseProviders.PostgreSql,
            "127.0.0.1", 15432, "smartsql_browser_test", "smartsql_browser_reader", ConnectionSslPolicy.Disable), string.Empty);
    }
}
