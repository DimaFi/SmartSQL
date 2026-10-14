using Npgsql;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>Creates and removes only uniquely named synthetic tables in the dedicated editing schema.</summary>
public sealed class PostgresEditingIntegrationTests
{
    private const string Schema = "smartsql_edit_test";

    [DedicatedPostgresFact]
    public async Task ReadOnlyIsDefaultAndEditingOptInCannotOverrideServerPermissions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        Assert.False(browser.EditingEnabled);
        Assert.False((await browser.GetEditCapabilitiesAsync(table)).CanUpdate);
        Assert.Null(Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows).EditHandle);
        await ErrorAsync("edit_not_allowed", browser.UpdateCellAsync(new(table, Guid.NewGuid(), "note", "after")));
        await using var reader = await PostgresDatabaseBrowser.OpenForEditingAsync(Profile("smartsql_browser_reader"), string.Empty);
        Assert.True(reader.EditingEnabled);
        Assert.False((await reader.GetEditCapabilitiesAsync(table)).CanUpdate);
        Assert.Null(Assert.Single((await reader.ReadPageAsync(table, 0, 10)).Rows).EditHandle);
        // An independently issued UPDATE verifies that SELECT grants did not accidentally grant write access.
        var readerSettings = PostgresDatabaseBrowser.ConnectionSettings(Profile("smartsql_browser_reader"));
        readerSettings.Options = "-c statement_timeout=15000 -c lock_timeout=3000";
        await using var connection = new NpgsqlConnection(readerSettings.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"UPDATE {Qualified(table)} SET note='after'", connection);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42501", denied.SqlState);
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task CompositeQuotedKeysEditExactlyOneCellAndPreserveNullEmptyWhitespace()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("\"key one\" integer, \"key\"\"two\" text, \"note\"\";--\" text, PRIMARY KEY(\"key one\",\"key\"\"two\")",
            "(1,'a','before'),(1,'b','other'),(2,'a','third')", "quoted \" ;-- ");
        await using var browser = await OpenEditorAsync();
        foreach (var value in new string?[] { string.Empty, "  \t\r\n 🌍  ", null, "'; UPDATE nowhere SET x=1; --" })
        {
            var page = await browser.ReadPageAsync(table, 0, 10);
            var row = page.Rows.Single(r => r.Values[0] == "1" && r.Values[1] == "a");
            var changed = await browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note\";--", value));
            Assert.Equal(new string?[] { "1", "a", value }, changed.Row.Values);
            Assert.Null(changed.Row.EditHandle);
            Assert.Equal(value, await fixture.ScalarAsync($"SELECT \"note\"\";--\" FROM {Qualified(table)} WHERE \"key one\"=1 AND \"key\"\"two\"='a'"));
            Assert.Equal("other", await fixture.ScalarAsync($"SELECT \"note\"\";--\" FROM {Qualified(table)} WHERE \"key one\"=1 AND \"key\"\"two\"='b'"));
        }
    }

    [DedicatedPostgresFact]
    public async Task LargeExactDecimalCommitsButServerRoundingRollsBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, amount numeric(60,20), note varchar(2)", "(1,1,'ok')");
        await using var browser = await OpenEditorAsync();
        const string exact = "1234567890123456789012345678901234567890.12345678901234567890";
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        var result = await browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "amount", exact));
        Assert.Equal(exact, result.Row.Values[1]);
        Assert.Equal(exact, await fixture.ScalarAsync($"SELECT amount::text FROM {Qualified(table)}"));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await ErrorAsync("edit_precision_loss", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "amount", "2.123456789012345678901")));
        Assert.Equal(exact, await fixture.ScalarAsync($"SELECT amount::text FROM {Qualified(table)}"));
        await ErrorAsync("edit_value_invalid", browser.UpdateCellAsync(new(table, row.EditHandle.Value, "note", "long")));
        Assert.Equal("ok", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task StaleChangedDeletedAndRestoredRowsCannotBeOverwritten()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text, other text", "(1,'before','original')");
        await using var browser = await OpenEditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"UPDATE {Qualified(table)} SET other='concurrent'");
        await ErrorAsync("edit_conflict", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "lost update")));
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"UPDATE {Qualified(table)} SET note='temporary'; UPDATE {Qualified(table)} SET note='before'");
        await ErrorAsync("edit_conflict", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "changed")));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"DELETE FROM {Qualified(table)}");
        await ErrorAsync("edit_conflict", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "resurrect")));
        Assert.Equal("0", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Qualified(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task PrimaryUniqueGeneratedAndUnsupportedColumnsStayReadOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, unique_note text UNIQUE, generated_note text GENERATED ALWAYS AS (unique_note || '!') STORED, document jsonb, note text",
            "(1,'unique',DEFAULT,'{}','before')");
        await using var browser = await OpenEditorAsync();
        var capabilities = await browser.GetEditCapabilitiesAsync(table);
        Assert.True(capabilities.CanUpdate);
        Assert.True(capabilities.Columns.Single(c => c.Name == "note").CanEdit);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        foreach (var name in new[] { "id", "unique_note", "generated_note", "document" })
        {
            Assert.False(capabilities.Columns.Single(c => c.Name == name).CanEdit);
            await ErrorAsync("edit_not_allowed", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, name, "changed")));
        }
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task UnsafeTablesRemainReadableButNeverReceiveEditingHandles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var keyless = await fixture.TableAsync("note text", "('readable')");
        var rls = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'readable')");
        await fixture.ExecuteAsync($"ALTER TABLE {Qualified(rls)} ENABLE ROW LEVEL SECURITY");
        var triggered = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'readable')");
        await fixture.ExecuteAsync($"CREATE TRIGGER test_trigger BEFORE UPDATE ON {Qualified(triggered)} FOR EACH ROW EXECUTE FUNCTION smartsql_edit_test.noop_trigger()");
        var check = await fixture.TableAsync("id integer PRIMARY KEY, note text CHECK(smartsql_edit_test.unsafe_check(note))", "(1,'readable')");
        var custom = await fixture.TableAsync("id integer PRIMARY KEY, note text, custom smartsql_edit_test.custom_value", "(1,'readable',ROW('custom'))");
        var parent = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'readable')");
        var child = await fixture.TableAsync($"extra text) INHERITS ({Qualified(parent)})", null, closeDefinition: false);
        var deferred = await fixture.TableAsync("id integer, note text, PRIMARY KEY(id) DEFERRABLE INITIALLY IMMEDIATE", "(1,'readable')");
        var generated = await fixture.TableAsync("id integer PRIMARY KEY, note text, calculated text GENERATED ALWAYS AS (smartsql_edit_test.unsafe_immutable(note)) STORED",
            "(1,'readable',DEFAULT)");
        var indexed = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'readable')");
        await fixture.ExecuteAsync($"CREATE INDEX ON {Qualified(indexed)} (smartsql_edit_test.unsafe_immutable(note))");
        var partial = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'readable')");
        await fixture.ExecuteAsync($"CREATE INDEX ON {Qualified(partial)} (note) WHERE smartsql_edit_test.unsafe_immutable_check(note)");
        await using var browser = await OpenEditorAsync();
        foreach (var table in new[] { keyless, rls, triggered, check, custom, parent, child, deferred, generated, indexed, partial })
        {
            var capabilities = await browser.GetEditCapabilitiesAsync(table);
            Assert.False(capabilities.CanUpdate);
            Assert.All(capabilities.Columns, column => Assert.False(column.CanEdit));
            Assert.All((await browser.ReadPageAsync(table, 0, 10)).Rows, row => Assert.Null(row.EditHandle));
        }
    }

    [DedicatedPostgresFact]
    public async Task VolatileBuiltinChecksBlockEditingWithoutAdvancingTheirSequence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sequence = await fixture.SequenceAsync();
        var random = await fixture.TableAsync("id integer PRIMARY KEY, note text CHECK(random() >= 0)", "(1,'before')");
        var advancing = await fixture.TableAsync($"id integer PRIMARY KEY, note text CHECK(nextval('{Qualified(sequence)}'::regclass) > 0)", "(1,'before')");
        Assert.Equal("1", await fixture.ScalarAsync($"SELECT last_value::text FROM {Qualified(sequence)}"));
        await using var browser = await OpenEditorAsync();
        foreach (var table in new[] { random, advancing })
        {
            Assert.False((await browser.GetEditCapabilitiesAsync(table)).CanUpdate);
            Assert.Null(Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows).EditHandle);
            await ErrorAsync("edit_handle_expired", browser.UpdateCellAsync(new(table, Guid.NewGuid(), "note", "changed")));
            Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
        }
        Assert.Equal("1", await fixture.ScalarAsync($"SELECT last_value::text FROM {Qualified(sequence)}"));
    }

    [DedicatedPostgresFact]
    public async Task HandlesCannotCrossConnectionsTablesRefreshSuccessOrDisposal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before'),(2,'second')");
        var otherTable = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'other')");
        await using var browser = await OpenEditorAsync();
        await using var other = await OpenEditorAsync();
        var page = await browser.ReadPageAsync(table, 0, 10);
        var handle = page.Rows[0].EditHandle!.Value;
        await ErrorAsync("edit_handle_expired", other.UpdateCellAsync(new(table, handle, "note", "wrong browser")));
        await ErrorAsync("edit_handle_expired", browser.UpdateCellAsync(new(otherTable, handle, "note", "wrong table")));
        await browser.GetColumnsAsync(table);
        await ErrorAsync("edit_handle_expired", browser.UpdateCellAsync(new(table, handle, "note", "refresh")));
        page = await browser.ReadPageAsync(table, 0, 10);
        handle = page.Rows[0].EditHandle!.Value;
        await browser.GetTablesAsync();
        await ErrorAsync("edit_handle_expired", browser.UpdateCellAsync(new(table, handle, "note", "catalog refresh")));
        page = await browser.ReadPageAsync(table, 0, 10);
        handle = page.Rows[0].EditHandle!.Value;
        await browser.ReadPageAsync(table, 1, 1);
        await ErrorAsync("edit_handle_expired", browser.UpdateCellAsync(new(table, handle, "note", "different page")));
        page = await browser.ReadPageAsync(table, 0, 10);
        await browser.UpdateCellAsync(new(table, page.Rows[0].EditHandle!.Value, "note", "saved"));
        foreach (var row in page.Rows)
            await ErrorAsync("edit_handle_expired", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "reuse")));
        var current = Assert.Single((await browser.ReadPageAsync(table, 0, 1)).Rows);
        await browser.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => browser.UpdateCellAsync(new(table, current.EditHandle!.Value, "note", "disposed")));
    }

    [DedicatedPostgresFact]
    public async Task SameTableMetadataChangesInvalidatePreviouslyConfirmedUpdate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await using var browser = await OpenEditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"ALTER TABLE {Qualified(table)} ADD COLUMN extra text");
        await ErrorAsync("edit_schema_changed", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "changed")));
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await fixture.ExecuteAsync($"ALTER TABLE {Qualified(table)} ALTER COLUMN note TYPE varchar(10)");
        await ErrorAsync("edit_schema_changed", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "changed")));
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task BuiltinTypedValuesCommitExactlyAndNativeIntegerOverflowRollsBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, small smallint, medium integer, big bigint, day date, flag boolean, amount numeric(60,20)",
            "(1,1,1,1,'2026-01-01',false,1)");
        await using var browser = await OpenEditorAsync();
        foreach (var (column, value) in new[] { ("small", "32767"), ("medium", "2147483647"),
                     ("big", "9223372036854775807"), ("day", "2026-10-14"), ("flag", "true") })
        {
            var page = await browser.ReadPageAsync(table, 0, 10);
            var row = Assert.Single(page.Rows);
            var result = await browser.UpdateCellAsync(new(table, row.EditHandle!.Value, column, value));
            Assert.Equal(value, result.Row.Values[page.Columns.Single(c => c.Name == column).Ordinal]);
            Assert.Equal(value, await fixture.ScalarAsync($"SELECT {PostgresBrowserSql.QuoteIdentifier(column)}::text FROM {Qualified(table)}"));
        }
        foreach (var (column, overflow, retained) in new[] { ("small", "32768", "32767"),
                     ("medium", "2147483648", "2147483647"), ("big", "9223372036854775808", "9223372036854775807") })
        {
            var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
            await ErrorAsync("edit_precision_loss", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, column, overflow)));
            Assert.Equal(retained, await fixture.ScalarAsync($"SELECT {PostgresBrowserSql.QuoteIdentifier(column)}::text FROM {Qualified(table)}"));
        }
        var nullable = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        var changed = await browser.UpdateCellAsync(new(table, nullable.EditHandle!.Value, "amount", null));
        Assert.Null(changed.Row.Values[6]);
        Assert.Null(await fixture.ScalarAsync($"SELECT amount::text FROM {Qualified(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task CancellingUpdateWhileRowIsLockedRollsBackWithoutChangingData()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text", "(1,'before')");
        await using var browser = await OpenEditorAsync();
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await using var blocker = await fixture.LockRowAsync(table);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var update = browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "cancelled"), cancellation.Token);
        // Observe the actual row-lock wait rather than assuming command progress from an elapsed delay.
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        var waiting = false;
        while (DateTimeOffset.UtcNow < deadline && !update.IsCompleted)
        {
            await fixture.ExecuteAsync("SELECT pg_catalog.pg_stat_clear_snapshot()");
            waiting = await fixture.ScalarAsync("SELECT count(*)::text FROM pg_catalog.pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%FOR UPDATE%'") != "0";
            if (waiting) break;
            await Task.Delay(20);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        Assert.True(waiting, "The update must reach the server's row-lock wait before cancellation.");
        await blocker.RollbackAsync();
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
        // A fresh confirmation remains usable after cancellation; no automatic retry was performed.
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "confirmed"));
        Assert.Equal("confirmed", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task BuiltinChecksRejectInvalidValuesWithoutChangingStoredData()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, amount integer CHECK(amount > 0), note text NOT NULL", "(1,1,'before')");
        await using var browser = await OpenEditorAsync();
        Assert.True((await browser.GetEditCapabilitiesAsync(table)).CanUpdate);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await ErrorAsync("edit_value_invalid", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "amount", "-1")));
        await ErrorAsync("edit_value_invalid", browser.UpdateCellAsync(new(table, row.EditHandle.Value, "note", null)));
        Assert.Equal("1", await fixture.ScalarAsync($"SELECT amount::text FROM {Qualified(table)}"));
        Assert.Equal("before", await fixture.ScalarAsync($"SELECT note FROM {Qualified(table)}"));
    }

    private static async Task ErrorAsync(string code, Task<DatabaseUpdateResult> operation)
    {
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => operation);
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }

    private static Task<PostgresDatabaseBrowser> OpenEditorAsync() => PostgresDatabaseBrowser.OpenForEditingAsync(Profile(), string.Empty);
    private static ConnectionProfile Profile(string username = "smartsql_browser_editor")
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432")
            throw new InvalidOperationException("Editing tests access only the dedicated loopback fixture on port 15432.");
        return new(Guid.NewGuid(), "Dedicated editing fixture", DatabaseProviders.PostgreSql, "127.0.0.1", 15432,
            "smartsql_browser_test", username, ConnectionSslPolicy.Disable);
    }

    private static string Qualified(DatabaseTableRef table) => PostgresEditingSql.Qualified(table);

    private sealed class Fixture(NpgsqlConnection connection) : IAsyncDisposable
    {
        private readonly List<DatabaseTableRef> _tables = [];
        private readonly List<DatabaseTableRef> _sequences = [];

        public static async Task<Fixture> CreateAsync()
        {
            // Direct DDL is restricted to our synthetic schema and dedicated non-superuser owner.
            var settings = PostgresDatabaseBrowser.ConnectionSettings(Profile());
            settings.Options = "-c statement_timeout=15000 -c lock_timeout=3000";
            var connection = new NpgsqlConnection(settings.ConnectionString);
            try { await connection.OpenAsync(); return new Fixture(connection); }
            catch { await connection.DisposeAsync(); throw; }
        }

        public async Task<DatabaseTableRef> TableAsync(string definition, string? rows, string prefix = "edit_", bool closeDefinition = true)
        {
            var table = new DatabaseTableRef(Schema, prefix + Guid.NewGuid().ToString("N"), DatabaseObjectKind.Table);
            await ExecuteAsync($"CREATE TABLE {Qualified(table)} ({definition}{(closeDefinition ? ")" : string.Empty)}");
            _tables.Add(table);
            if (rows is not null) await ExecuteAsync($"INSERT INTO {Qualified(table)} VALUES {rows}");
            return table;
        }

        public async Task<DatabaseTableRef> SequenceAsync()
        {
            var sequence = new DatabaseTableRef(Schema, "edit_sequence_" + Guid.NewGuid().ToString("N"), DatabaseObjectKind.Table);
            await ExecuteAsync($"CREATE SEQUENCE {Qualified(sequence)}");
            _sequences.Add(sequence);
            return sequence;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<NpgsqlTransaction> LockRowAsync(DatabaseTableRef table)
        {
            var transaction = await connection.BeginTransactionAsync();
            try
            {
                await using var command = new NpgsqlCommand($"SELECT id FROM {Qualified(table)} WHERE id=1 FOR UPDATE", connection, transaction);
                await command.ExecuteScalarAsync();
                return transaction;
            }
            catch { await transaction.DisposeAsync(); throw; }
        }

        public async Task<string?> ScalarAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? null : (string)value;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var table in _tables.AsEnumerable().Reverse())
                    await ExecuteAsync($"DROP TABLE IF EXISTS {Qualified(table)}");
                foreach (var sequence in _sequences.AsEnumerable().Reverse())
                    await ExecuteAsync($"DROP SEQUENCE IF EXISTS {Qualified(sequence)}");
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
