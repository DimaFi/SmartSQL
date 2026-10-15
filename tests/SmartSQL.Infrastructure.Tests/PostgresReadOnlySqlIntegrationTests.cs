using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class PostgresReadOnlySqlIntegrationTests
{
    [DedicatedPostgresFact]
    public async Task NativeTextKeepsLargeNumericDuplicateNamesNullEmptyBinaryAndReadOnlyState()
    {
        await using var sql = await Open();
        var result = await sql.ExecuteAsync(new("SELECT 1234567890123456789012345678901234567890.12345678901234567890::numeric AS same, NULL::text AS same, ''::text AS same, decode('00ff','hex') AS bytes, current_setting('transaction_read_only') AS mode"));
        Assert.Equal(new[] { "same", "same", "same", "bytes", "mode" }, result.Columns.Select(c => c.Name));
        Assert.Equal(Enumerable.Range(0, 5), result.Columns.Select(c => c.Ordinal));
        Assert.Equal("numeric", result.Columns[0].NativeType);
        Assert.Equal(new string?[] { "1234567890123456789012345678901234567890.12345678901234567890", null, "", "\\x00ff", "on" }, Assert.Single(result.Rows).Cells.Select(c => c.Value));
        Assert.False(result.Truncated);
    }

    [DedicatedPostgresFact]
    public async Task NativeParserAcceptsCommentsSemicolonsInLiteralsCteAndLimitSentinel()
    {
        await using var sql = await Open();
        var result = await sql.ExecuteAsync(new("/* header */ WITH n AS (SELECT generate_series(1,5) AS i) SELECT i, '; -- literal' AS note FROM n ORDER BY i; -- tail", 2));
        Assert.Equal(new[] { "1", "2" }, result.Rows.Select(r => r.Cells[0].Value));
        Assert.True(result.Truncated);
        Assert.All(result.Rows, r => Assert.Equal("; -- literal", r.Cells[1].Value));
        var empty = await sql.ExecuteAsync(new("SELECT 1 AS empty WHERE false"));
        Assert.Single(empty.Columns);
        Assert.Empty(empty.Rows);
        Assert.False(empty.Truncated);
    }

    [DedicatedPostgresFact]
    public async Task NativeSingleStatementAndCursorPolicyRejectControlAndWritesWithoutPoisoningSession()
    {
        // This dedicated synthetic admin has write rights, so rejection does not rely on a read-only login.
        await using var sql = await Open("smartsql_test_admin");
        foreach (var text in new[]
        {
            "SELECT 1; SELECT 2", "SELECT 1; SET default_transaction_read_only=off",
            "COMMIT", "BEGIN", "SET transaction_read_only=off", "SHOW transaction_read_only",
            "CREATE TEMP TABLE smartsql_sql_probe(id int)", "SELECT 1 INTO TEMP smartsql_sql_probe",
            "WITH changed AS (DELETE FROM public.browser_rows RETURNING id) SELECT * FROM changed",
            "DELETE FROM public.browser_rows RETURNING id", "SELECT * FROM public.browser_rows FOR UPDATE",
            "SELECT $1", "EXPLAIN SELECT 1"
        })
        {
            var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new(text)));
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(text, failure.ToString());
            Assert.Equal("on", Assert.Single((await sql.ExecuteAsync(new("SELECT current_setting('transaction_read_only')"))).Rows).Cells[0].Value);
        }
        Assert.Equal("205", Assert.Single((await sql.ExecuteAsync(new("SELECT count(*) FROM public.browser_rows"))).Rows).Cells[0].Value);
    }

    [DedicatedPostgresFact]
    public async Task SessionSettingFunctionsCannotGrantWritePermissionToCurrentOrNextQuery()
    {
        await using var sql = await Open("smartsql_test_admin");
        await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("SELECT set_config('transaction_read_only','off',true)")));
        Assert.Equal("off", Assert.Single((await sql.ExecuteAsync(new("SELECT set_config('default_transaction_read_only','off',false)"))).Rows).Cells[0].Value);
        Assert.Equal("on", Assert.Single((await sql.ExecuteAsync(new("SELECT current_setting('transaction_read_only')"))).Rows).Cells[0].Value);
    }

    [DedicatedPostgresFact]
    public async Task NativeJsonArraysDatesAndUtcTimestampAreServerText()
    {
        await using var sql = await Open();
        var result = await sql.ExecuteAsync(new("SELECT '[1,null]'::jsonb, ARRAY[1,2]::integer[], DATE '2026-10-15', TIMESTAMPTZ '2026-10-15 12:34:56.123456+03'"));
        Assert.Equal(new[] { "[1, null]", "{1,2}", "2026-10-15", "2026-10-15 09:34:56.123456+00" }, Assert.Single(result.Rows).Cells.Select(c => c.Value));
    }

    [DedicatedPostgresFact]
    public async Task OversizeValuesFailExplicitlyAndNextQueryStillWorks()
    {
        await using var sql = await Open();
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("SELECT repeat('x',1000001)")));
        Assert.Equal("cell_too_large", failure.Code);
        Assert.Equal("42", Assert.Single((await sql.ExecuteAsync(new("VALUES(42)"))).Rows).Cells[0].Value);
    }

    [DedicatedPostgresFact]
    public async Task CancellationStopsServerWorkAndSessionCanBeReused()
    {
        await using var sql = await Open();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sql.ExecuteAsync(new("SELECT pg_sleep(10)"), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.Equal("1", Assert.Single((await sql.ExecuteAsync(new("SELECT 1"))).Rows).Cells[0].Value);
    }

    [DedicatedPostgresFact]
    public async Task DisposalCancelsRunningAndQueuedQueriesAndRejectsNewWork()
    {
        var sql = await Open();
        var running = sql.ExecuteAsync(new("SELECT pg_sleep(10)"));
        var queued = sql.ExecuteAsync(new("SELECT 2"));
        var disposal = sql.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(8)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(8)));
        await disposal.WaitAsync(TimeSpan.FromSeconds(8));
        await sql.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sql.ExecuteAsync(new("SELECT 3")));
    }

    private static Task<PostgresReadOnlySqlExecutor> Open(string username = "smartsql_browser_reader")
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432")
            throw new InvalidOperationException("Only the dedicated loopback PostgreSQL fixture is permitted.");
        return PostgresReadOnlySqlExecutor.OpenAsync(new ConnectionProfile(Guid.NewGuid(), "SQL integration fixture", DatabaseProviders.PostgreSql,
            "127.0.0.1", 15432, "smartsql_browser_test", username, ConnectionSslPolicy.Disable), string.Empty);
    }
}
