using Npgsql;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>Only synthetic uniquely named tables in the existing dedicated loopback fixture are mutated.</summary>
public sealed class PostgresDeletionIntegrationTests
{
    [DedicatedPostgresFact]
    public async Task DeleteOnlyPermissionWorksWithoutUpdateWhileReadOnlySessionsStayProtected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'delete only'),(2,'remaining')");
        await using var readOnly = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        Assert.False((await readOnly.GetDeleteCapabilitiesAsync(table)).CanDelete);
        Assert.All((await readOnly.ReadPageAsync(table, 0, 10)).Rows, r => Assert.Null(r.DeleteHandle));
        await ErrorAsync("delete_not_allowed", readOnly.DeleteRowAsync(new(table, Guid.NewGuid())));
        await using var reader = await EditorAsync("smartsql_browser_reader");
        Assert.False((await reader.GetDeleteCapabilitiesAsync(table)).CanDelete);
        Assert.All((await reader.ReadPageAsync(table, 0, 10)).Rows, r => Assert.Null(r.DeleteHandle));
        await fixture.ExecuteAsync($"GRANT DELETE ON {Q(table)} TO smartsql_browser_reader");
        Assert.True((await reader.GetDeleteCapabilitiesAsync(table)).CanDelete);
        Assert.False((await reader.GetEditCapabilitiesAsync(table)).CanUpdate);
        var rows = (await reader.ReadPageAsync(table, 0, 10)).Rows;
        Assert.All(rows, r => Assert.Null(r.EditHandle));
        await reader.DeleteRowAsync(new(table, rows[0].DeleteHandle!.Value));
        Assert.Equal("2", await fixture.ScalarAsync($"SELECT id::text FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task QuotedCompositeNativeKeysDeleteExactlyOneRowWithoutNumericConversion()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string exact = "1234567890123456789012345678901234567890.12345678901234567890";
        var table = await fixture.TableAsync("\"ключ \"\" .\" numeric(60,20), \"tenant;--\" text, note text, payload bytea, PRIMARY KEY(\"tenant;--\",\"ключ \"\" .\")",
            $"({exact},'a','  🌍  ',decode('ff00','hex')),({exact},'b','other',NULL),(1,'a','third',NULL)", "delete \" ;-- ");
        await using var browser = await EditorAsync();
        Assert.True((await browser.GetDeleteCapabilitiesAsync(table)).CanDelete);
        var page = await browser.ReadPageAsync(table, 0, 10);
        var selected = page.Rows.Single(r => r.Values[0] == exact && r.Values[1] == "a");
        Assert.Equal("\\xff00", selected.Values[3]);
        await browser.DeleteRowAsync(new(table, selected.DeleteHandle!.Value));
        Assert.Equal("2", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
        Assert.Equal("other", await fixture.ScalarAsync($"SELECT note FROM {Q(table)} WHERE \"tenant;--\"='b'"));
        Assert.Equal("third", await fixture.ScalarAsync($"SELECT note FROM {Q(table)} WHERE \"ключ \"\" .\"=1"));
        foreach (var row in page.Rows)
            await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
    }

    [DedicatedPostgresFact]
    public async Task ChangedDeletedAndDeletedReinsertedRowsCannotUseOldConfirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await using var browser = await EditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"UPDATE {Q(table)} SET note='changed'");
        await ErrorAsync("delete_conflict", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.DeleteHandle.Value)));
        Assert.Equal("changed", await fixture.ScalarAsync($"SELECT note FROM {Q(table)}"));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"UPDATE {Q(table)} SET note='temporary'; UPDATE {Q(table)} SET note='changed'");
        await ErrorAsync("delete_conflict", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"DELETE FROM {Q(table)}; INSERT INTO {Q(table)} VALUES(1,'changed')");
        await ErrorAsync("delete_conflict", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        Assert.Equal("changed", await fixture.ScalarAsync($"SELECT note FROM {Q(table)}"));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"DELETE FROM {Q(table)}");
        await ErrorAsync("delete_conflict", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
    }

    [DedicatedPostgresFact]
    public async Task MutatingDisplayedValuesCannotReplaceThePrivateOriginalDeletionIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'original'),(2,'other')");
        await using var browser = await EditorAsync();
        var row = (await browser.ReadPageAsync(table, 0, 10)).Rows.Single(r => r.Values[0] == "1");
        var displayed = Assert.IsType<string?[]>(row.Values);
        displayed[0] = "2";
        displayed[1] = "other";
        await browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value));
        Assert.Equal("2", await fixture.ScalarAsync($"SELECT id::text FROM {Q(table)}"));
        Assert.Equal("other", await fixture.ScalarAsync($"SELECT note FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task HandlesAreSeparateFromEditsAndCannotCrossBrowserTableRefreshOrDisposal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        var otherTable = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'other')");
        await using var browser = await EditorAsync();
        await using var other = await EditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        Assert.NotNull(row.EditHandle);
        Assert.NotNull(row.DeleteHandle);
        Assert.NotEqual(row.EditHandle, row.DeleteHandle);
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.EditHandle!.Value)));
        await ErrorAsync("delete_handle_expired", other.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(otherTable, row.DeleteHandle.Value)));
        await browser.GetColumnsAsync(table);
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.DeleteHandle.Value)));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await browser.GetTablesAsync();
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await browser.ReadPageAsync(otherTable, 0, 10);
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await browser.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        Assert.Equal("1", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task SchemaAndIncomingForeignKeyChangesRejectPreviouslyConfirmedDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await using var browser = await EditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"ALTER TABLE {Q(table)} ADD COLUMN extra text");
        await ErrorAsync("delete_schema_changed", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.TableAsync($"id integer PRIMARY KEY, parent integer REFERENCES {Q(table)}(id) ON DELETE CASCADE", "(1,1)");
        await ErrorAsync("delete_schema_changed", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        Assert.Equal("1", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task CascadingSetNullAndSetDefaultIncomingReferencesBlockDeletionWithoutSideEffects()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var browser = await EditorAsync();
        foreach (var action in new[] { "CASCADE", "SET NULL", "SET DEFAULT" })
        {
            var parent = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'parent'),(2,'default')");
            var child = await fixture.TableAsync($"id integer PRIMARY KEY, parent integer DEFAULT 2 REFERENCES {Q(parent)}(id) ON DELETE {action}", "(1,1)");
            Assert.False((await browser.GetDeleteCapabilitiesAsync(parent)).CanDelete);
            Assert.All((await browser.ReadPageAsync(parent, 0, 10)).Rows, r => Assert.Null(r.DeleteHandle));
            Assert.Equal("2", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(parent)}"));
            Assert.Equal("1", await fixture.ScalarAsync($"SELECT parent::text FROM {Q(child)}"));
        }
    }

    [DedicatedPostgresFact]
    public async Task RestrictAndNoActionIncomingReferencesRemainProtectedWhileDeletingChildIsAllowed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var browser = await EditorAsync();
        foreach (var action in new[] { "RESTRICT", "NO ACTION" })
        {
            var parent = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'referenced'),(2,'free')");
            var child = await fixture.TableAsync($"id integer PRIMARY KEY, parent integer REFERENCES {Q(parent)}(id) ON DELETE {action}", "(1,1)");
            var capabilities = await browser.GetDeleteCapabilitiesAsync(parent);
            Assert.False(capabilities.CanDelete);
            Assert.Contains(capabilities.Diagnostics, d => d.Code == "delete_incoming_foreign_keys");
            Assert.All((await browser.ReadPageAsync(parent, 0, 10)).Rows, r => Assert.Null(r.DeleteHandle));
            Assert.Equal("2", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(parent)}"));
            Assert.True((await browser.GetDeleteCapabilitiesAsync(child)).CanDelete);
            var row = Assert.Single((await browser.ReadPageAsync(child, 0, 10)).Rows);
            await browser.DeleteRowAsync(new(child, row.DeleteHandle!.Value));
            Assert.Equal("0", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(child)}"));
            Assert.Equal("2", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(parent)}"));
        }
    }

    [DedicatedPostgresFact]
    public async Task UserDeleteTriggersRulesRlsInheritanceAndPartitioningRemainProtected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trigger = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await fixture.ExecuteAsync($"CREATE TRIGGER unsafe BEFORE DELETE ON {Q(trigger)} FOR EACH ROW EXECUTE FUNCTION smartsql_edit_test.noop_trigger()");
        var rule = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await fixture.ExecuteAsync($"CREATE RULE no_delete AS ON DELETE TO {Q(rule)} DO INSTEAD NOTHING");
        var rls = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await fixture.ExecuteAsync($"ALTER TABLE {Q(rls)} ENABLE ROW LEVEL SECURITY");
        var parent = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        var child = await fixture.TableAsync("extra text", null, suffix: $" INHERITS ({Q(parent)})");
        var partitioned = await fixture.TableAsync("id integer PRIMARY KEY, note text", null, suffix: " PARTITION BY RANGE(id)", kind: DatabaseObjectKind.PartitionedTable);
        var partition = await fixture.PartitionAsync(partitioned);
        var noKey = await fixture.TableAsync("note text", "('before')");
        await using var browser = await EditorAsync();
        foreach (var table in new[] { trigger, rule, rls, parent, child, partitioned, partition, noKey })
        {
            Assert.False((await browser.GetDeleteCapabilitiesAsync(table)).CanDelete);
            Assert.All((await browser.ReadPageAsync(table, 0, 10)).Rows, r => Assert.Null(r.DeleteHandle));
        }
    }

    [DedicatedPostgresFact]
    public async Task CancellationWhileWaitingForProtectiveLockLeavesRowsAndAllowsFreshConfirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await using var browser = await EditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await using var blocker = await fixture.LockAsync(table);
        using var cancellation = new CancellationTokenSource();
        var deletion = browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value), cancellation.Token);
        var waiting = false;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline && !deletion.IsCompleted)
        {
            await fixture.ExecuteAsync("SELECT pg_catalog.pg_stat_clear_snapshot()", blocker);
            waiting = await fixture.ScalarAsync("SELECT count(*)::text FROM pg_catalog.pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE 'LOCK TABLE ONLY%'") != "0";
            if (waiting) break;
            await Task.Delay(20);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deletion);
        Assert.True(waiting, "Deletion must reach a real protective lock wait.");
        await blocker.RollbackAsync();
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Q(table)}"));
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Q(table)}"));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value));
        Assert.Equal("0", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task ProtectiveGuardBlocksNewIncomingForeignKeyWhileDeleteWaitsForRowLock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        var child = await fixture.TableAsync("id integer PRIMARY KEY, parent integer");
        await using var browser = await EditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await using var blocker = await fixture.LockRowAsync(table);
        using var deleteCancellation = new CancellationTokenSource();
        var deletion = browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value), deleteCancellation.Token);
        var deleteWaiting = await fixture.WaitAsync("SELECT count(*)::text FROM pg_catalog.pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE 'DELETE FROM ONLY%'", deletion, blocker);
        using var ddlCancellation = new CancellationTokenSource();
        Task? ddl = null;
        var foreignKeyWaiting = false;
        try
        {
            if (deleteWaiting)
            {
                ddl = fixture.AddForeignKeyAsync(child, table, ddlCancellation.Token);
                foreignKeyWaiting = await fixture.WaitAsync($"SELECT count(*)::text FROM pg_catalog.pg_locks l JOIN pg_catalog.pg_stat_activity a ON a.pid=l.pid WHERE a.usename=current_user AND l.relation='{Q(table)}'::regclass AND l.mode='ShareRowExclusiveLock' AND NOT l.granted AND a.query LIKE 'ALTER TABLE%ADD CONSTRAINT%'", ddl, blocker);
            }
        }
        finally
        {
            ddlCancellation.Cancel();
            deleteCancellation.Cancel();
            if (ddl is not null) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ddl);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deletion);
            await blocker.RollbackAsync();
        }
        Assert.True(deleteWaiting, "DELETE must hold its protective table guard while waiting on the independently locked row.");
        Assert.True(foreignKeyWaiting, "Concurrent ADD FOREIGN KEY must wait for that target guard.");
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Q(table)}"));
        Assert.Equal("0", await fixture.ScalarAsync($"SELECT count(*)::text FROM pg_catalog.pg_constraint WHERE conrelid='{Q(child)}'::regclass AND contype='f'"));
        await ErrorAsync("delete_handle_expired", browser.DeleteRowAsync(new(table, row.DeleteHandle!.Value)));
    }

    private static async Task ErrorAsync(string code, Task<DatabaseDeleteResult> operation)
    {
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => operation);
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }
    private static Task<PostgresDatabaseBrowser> EditorAsync(string username = "smartsql_browser_editor") => PostgresDatabaseBrowser.OpenForEditingAsync(Profile(username), string.Empty);
    private static ConnectionProfile Profile(string username = "smartsql_browser_editor")
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432") throw new InvalidOperationException("Deletion tests use only the dedicated loopback fixture on port 15432.");
        return new(Guid.NewGuid(), "Dedicated deletion fixture", DatabaseProviders.PostgreSql, "127.0.0.1", 15432, "smartsql_browser_test", username, ConnectionSslPolicy.Disable);
    }
    private static string Q(DatabaseTableRef table) => PostgresEditingSql.Qualified(table);
    private sealed class Fixture(NpgsqlConnection connection) : IAsyncDisposable
    {
        private readonly List<DatabaseTableRef> _tables = [];
        public static async Task<Fixture> CreateAsync()
        {
            var settings = PostgresDatabaseBrowser.ConnectionSettings(Profile());
            settings.Options = "-c statement_timeout=15000 -c lock_timeout=3000";
            var connection = new NpgsqlConnection(settings.ConnectionString);
            try { await connection.OpenAsync(); return new(connection); }
            catch { await connection.DisposeAsync(); throw; }
        }
        public async Task<DatabaseTableRef> TableAsync(string definition, string? rows = null, string prefix = "delete_", string suffix = "", DatabaseObjectKind kind = DatabaseObjectKind.Table)
        {
            var table = new DatabaseTableRef("smartsql_edit_test", prefix + Guid.NewGuid().ToString("N"), kind);
            await ExecuteAsync($"CREATE TABLE {Q(table)} ({definition}){suffix}");
            _tables.Add(table);
            if (rows is not null) await ExecuteAsync($"INSERT INTO {Q(table)} VALUES {rows}");
            return table;
        }
        public async Task<DatabaseTableRef> PartitionAsync(DatabaseTableRef parent)
        {
            var table = new DatabaseTableRef("smartsql_edit_test", "delete_partition_" + Guid.NewGuid().ToString("N"), DatabaseObjectKind.Table);
            await ExecuteAsync($"CREATE TABLE {Q(table)} PARTITION OF {Q(parent)} FOR VALUES FROM (0) TO (10)");
            _tables.Add(table);
            return table;
        }
        public async Task ExecuteAsync(string sql, NpgsqlTransaction? transaction = null)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        public async Task<string?> ScalarAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? null : (string)value;
        }
        public async Task<NpgsqlTransaction> LockAsync(DatabaseTableRef table)
        {
            var transaction = await connection.BeginTransactionAsync();
            try { await ExecuteAsync($"LOCK TABLE {Q(table)} IN ROW EXCLUSIVE MODE", transaction); return transaction; }
            catch { await transaction.DisposeAsync(); throw; }
        }
        public async Task<NpgsqlTransaction> LockRowAsync(DatabaseTableRef table)
        {
            var transaction = await connection.BeginTransactionAsync();
            try { await ExecuteAsync($"SELECT id FROM {Q(table)} WHERE id=1 FOR UPDATE", transaction); return transaction; }
            catch { await transaction.DisposeAsync(); throw; }
        }
        public async Task<bool> WaitAsync(string sql, Task operation, NpgsqlTransaction transaction)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline && !operation.IsCompleted)
            {
                await ExecuteAsync("SELECT pg_catalog.pg_stat_clear_snapshot()", transaction);
                if (await ScalarAsync(sql) != "0") return true;
                await Task.Delay(20);
            }
            return false;
        }
        public async Task AddForeignKeyAsync(DatabaseTableRef child, DatabaseTableRef parent, CancellationToken token)
        {
            var settings = PostgresDatabaseBrowser.ConnectionSettings(Profile());
            settings.Options = "-c statement_timeout=15000 -c lock_timeout=10000";
            await using var ddlConnection = new NpgsqlConnection(settings.ConnectionString);
            await ddlConnection.OpenAsync(token);
            await using var command = new NpgsqlCommand($"ALTER TABLE {Q(child)} ADD CONSTRAINT incoming FOREIGN KEY(parent) REFERENCES {Q(parent)}(id) ON DELETE CASCADE", ddlConnection);
            await command.ExecuteNonQueryAsync(token);
        }
        public async ValueTask DisposeAsync()
        {
            try { foreach (var table in _tables.AsEnumerable().Reverse()) await ExecuteAsync($"DROP TABLE IF EXISTS {Q(table)}"); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
