using Npgsql;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>One-row insertion is tested only on unique synthetic objects in the dedicated loopback schema.</summary>
public sealed class PostgresInsertionIntegrationTests
{
    private const string Schema = "smartsql_edit_test";

    [DedicatedPostgresFact]
    public async Task IdentityDefaultsGeneratedColumnsAndExplicitNullEmptyWhitespaceRemainDistinct()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text NOT NULL, note text, tag text DEFAULT 'server', derived text GENERATED ALWAYS AS (upper(name)) STORED");
        await using var browser = await EditorAsync();
        var caps = await browser.GetInsertCapabilitiesAsync(table);
        Assert.True(caps.CanInsert);
        Assert.Contains(caps.Diagnostics, d => d.Code == "sequence_gaps");
        Assert.True(caps.Columns.Single(c => c.Column.Name == "id").MustUseDefault);
        Assert.True(caps.Columns.Single(c => c.Column.Name == "derived").MustUseDefault);
        Assert.False(caps.Columns.Single(c => c.Column.Name == "name").CanUseDefault);
        var first = await browser.InsertRowAsync(Request(caps, Value("name", "  alice  "), Null("note")));
        Assert.Equal("  alice  ", Cell(caps, first, "name"));
        Assert.Null(Cell(caps, first, "note"));
        Assert.Equal("server", Cell(caps, first, "tag"));
        Assert.Equal("  ALICE  ", Cell(caps, first, "derived"));
        await ErrorAsync("insert_confirmation_expired", browser.InsertRowAsync(Request(caps, Value("name", "repeat"))));
        caps = await browser.GetInsertCapabilitiesAsync(table);
        var second = await browser.InsertRowAsync(Request(caps, Value("name", "second"), Value("note", ""), Value("tag", "  ")));
        Assert.Equal("", Cell(caps, second, "note"));
        Assert.Equal("  ", Cell(caps, second, "tag"));
        Assert.Equal("2", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
        caps = await browser.GetInsertCapabilitiesAsync(table);
        await ErrorAsync("insert_value_invalid", browser.InsertRowAsync(Request(caps, Value("id", "999"), Value("name", "bad"))));
        await ErrorAsync("insert_value_invalid", browser.InsertRowAsync(Request(caps, Null("name"))));
        Assert.Equal("2", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task NativeNumbersDatesBooleansKeysAndUniqueFieldsCanBeInsertedWithoutPrecisionLoss()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("tenant text, id integer, token text UNIQUE, amount numeric(60,20), day date, enabled boolean, PRIMARY KEY(tenant,id)");
        await using var browser = await EditorAsync();
        var caps = await browser.GetInsertCapabilitiesAsync(table);
        Assert.True(caps.CanInsert);
        Assert.True(caps.Columns.Single(c => c.Column.Name == "tenant").Column.CanEdit);
        Assert.True(caps.Columns.Single(c => c.Column.Name == "token").Column.CanEdit);
        const string amount = "1234567890123456789012345678901234567890.12345678901234567890";
        var result = await browser.InsertRowAsync(Request(caps, Value("tenant", "quoted ' 🌍"), Value("id", "1"),
            Value("token", "unique"), Value("amount", amount), Value("day", "2026-10-14"), Value("enabled", "Да")));
        Assert.Equal(amount, Cell(caps, result, "amount"));
        Assert.Equal("2026-10-14", Cell(caps, result, "day"));
        Assert.Equal("true", Cell(caps, result, "enabled"));
        Assert.Equal("quoted ' 🌍", Cell(caps, result, "tenant"));
    }

    [DedicatedPostgresFact]
    public async Task RoundingOverflowTruncationDuplicatesAndChecksRollbackTheEntireRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, amount numeric(5,2), note varchar(3), positive integer CHECK(positive > 0)");
        await using var browser = await EditorAsync();
        var caps = await browser.GetInsertCapabilitiesAsync(table);
        await ErrorAsync("insert_precision_loss", browser.InsertRowAsync(Request(caps, Value("id", "1"), Value("amount", "1.234"))));
        await ErrorAsync("insert_precision_loss", browser.InsertRowAsync(Request(caps, Value("id", "2147483648"))));
        await ErrorAsync("insert_value_invalid", browser.InsertRowAsync(Request(caps, Value("id", "1"), Value("note", "long"))));
        await ErrorAsync("insert_value_invalid", browser.InsertRowAsync(Request(caps, Value("id", "1"), Value("positive", "-1"))));
        Assert.Equal("0", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
        await browser.InsertRowAsync(Request(caps, Value("id", "1"), Value("amount", "1.20"), Value("note", "ok")));
        caps = await browser.GetInsertCapabilitiesAsync(table);
        await ErrorAsync("insert_value_invalid", browser.InsertRowAsync(Request(caps, Value("id", "1"))));
        Assert.Equal("1", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task ReadOnlyOptInAndInsertOnlyPrivilegesAreIndependentOfUpdatePermission()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text");
        await using var readOnly = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        Assert.False((await readOnly.GetInsertCapabilitiesAsync(table)).CanInsert);
        await ErrorAsync("insert_not_allowed", readOnly.InsertRowAsync(new(table, Guid.NewGuid(), [Value("id", "1")])));
        await using var reader = await EditorAsync("smartsql_browser_reader");
        Assert.False((await reader.GetInsertCapabilitiesAsync(table)).CanInsert);
        await fixture.ExecuteAsync($"GRANT INSERT ON {Q(table)} TO smartsql_browser_reader");
        var caps = await reader.GetInsertCapabilitiesAsync(table);
        Assert.False(caps.CanInsert);
        Assert.Contains(caps.Diagnostics, d => d.Code == "insert_protective_lock_required");
        await fixture.GrantProtectiveLockAsync(table);
        caps = await reader.GetInsertCapabilitiesAsync(table);
        Assert.True(caps.CanInsert);
        Assert.False((await reader.GetEditCapabilitiesAsync(table)).CanUpdate);
        await reader.InsertRowAsync(Request(caps, Value("id", "1"), Value("note", "INSERT only")));
        Assert.Equal("INSERT only", await fixture.ScalarAsync($"SELECT note FROM {Q(table)} WHERE id=1"));
    }

    [DedicatedPostgresFact]
    public async Task OwnedSerialDefaultIsAllowedButUnownedAndUserDefaultsCannotExecute()
    {
        await using var fixture = await Fixture.CreateAsync();
        var serial = await fixture.TableAsync("id bigserial PRIMARY KEY, note text UNIQUE DEFAULT 'server'");
        await using var browser = await EditorAsync();
        var caps = await browser.GetInsertCapabilitiesAsync(serial);
        Assert.True(caps.CanInsert);
        Assert.True(caps.Columns.Single(c => c.Column.Name == "id").CanUseDefault);
        Assert.Contains(caps.Diagnostics, d => d.Code == "sequence_gaps");
        var result = await browser.InsertRowAsync(Request(caps));
        Assert.Equal("1", Cell(caps, result, "id"));
        Assert.Equal("server", Cell(caps, result, "note"));
        caps = await browser.GetInsertCapabilitiesAsync(serial);
        await ErrorAsync("insert_value_invalid", browser.InsertRowAsync(Request(caps)));
        Assert.Equal("1", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(serial)}"));
        result = await browser.InsertRowAsync(Request(caps, Value("note", "second")));
        Assert.Equal("3", Cell(caps, result, "id")); // Failed row rolled back; nextval intentionally did not.
        var sequence = await fixture.SequenceAsync();
        var unsafeTable = await fixture.TableAsync($"id integer PRIMARY KEY, note bigint DEFAULT nextval('{Q(sequence)}'::regclass), text_note text DEFAULT smartsql_edit_test.unsafe_immutable('secret')");
        caps = await browser.GetInsertCapabilitiesAsync(unsafeTable);
        Assert.True(caps.CanInsert); // Explicit supported values safely bypass both defaults.
        Assert.False(caps.Columns.Single(c => c.Column.Name == "note").CanUseDefault);
        Assert.False(caps.Columns.Single(c => c.Column.Name == "text_note").CanUseDefault);
        await ErrorAsync("insert_value_invalid", browser.InsertRowAsync(Request(caps, Value("id", "1"))));
        Assert.Equal("false", await fixture.ScalarAsync($"SELECT is_called::text FROM {Q(sequence)}"));
        await browser.InsertRowAsync(Request(caps, Value("id", "1"), Value("note", "9"), Value("text_note", "explicit")));
        Assert.Equal("false", await fixture.ScalarAsync($"SELECT is_called::text FROM {Q(sequence)}"));
    }

    [DedicatedPostgresFact]
    public async Task IdentityRequiresSequenceRightsAndUnsupportedRequiredColumnsBlockInsertion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, note text");
        await fixture.ExecuteAsync($"GRANT INSERT ON {Q(table)} TO smartsql_browser_reader");
        await fixture.GrantProtectiveLockAsync(table);
        await using var reader = await EditorAsync("smartsql_browser_reader");
        Assert.False((await reader.GetInsertCapabilitiesAsync(table)).CanInsert);
        var sequence = await fixture.ScalarAsync($"SELECT pg_get_serial_sequence('{Q(table)}','id')");
        Assert.NotNull(sequence);
        await fixture.ExecuteAsync($"GRANT USAGE ON SEQUENCE {sequence} TO smartsql_browser_reader");
        var caps = await reader.GetInsertCapabilitiesAsync(table);
        Assert.True(caps.CanInsert);
        await reader.InsertRowAsync(Request(caps, Value("note", "allowed")));
        var unsupported = await fixture.TableAsync("id integer PRIMARY KEY, document json NOT NULL");
        await using var browser = await EditorAsync();
        Assert.False((await browser.GetInsertCapabilitiesAsync(unsupported)).CanInsert);
        var optional = await fixture.TableAsync("id integer PRIMARY KEY, document json");
        caps = await browser.GetInsertCapabilitiesAsync(optional);
        Assert.True(caps.CanInsert);
        var result = await browser.InsertRowAsync(Request(caps, Value("id", "1")));
        Assert.Null(Cell(caps, result, "document"));
    }

    [DedicatedPostgresFact]
    public async Task ConfirmationSurvivesPageReadButDetectsSchemaChangeReplacementAndDifferentTable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text DEFAULT 'first'");
        var other = await fixture.TableAsync("id integer PRIMARY KEY, note text");
        await using var browser = await EditorAsync();
        var caps = await browser.GetInsertCapabilitiesAsync(table);
        await browser.ReadPageAsync(table, 0, 10);
        await browser.InsertRowAsync(Request(caps, Value("id", "1")));
        caps = await browser.GetInsertCapabilitiesAsync(table);
        await fixture.ExecuteAsync($"ALTER TABLE {Q(table)} ALTER COLUMN note SET DEFAULT 'second'");
        await ErrorAsync("insert_schema_changed", browser.InsertRowAsync(Request(caps, Value("id", "2"))));
        var previous = await browser.GetInsertCapabilitiesAsync(table);
        caps = await browser.GetInsertCapabilitiesAsync(table);
        await ErrorAsync("insert_confirmation_expired", browser.InsertRowAsync(Request(previous, Value("id", "2"))));
        await ErrorAsync("insert_confirmation_expired", browser.InsertRowAsync(new(other, caps.ConfirmationHandle!.Value, [Value("id", "2")])));
        var result = await browser.InsertRowAsync(Request(caps, Value("id", "2")));
        Assert.Equal("second", Cell(caps, result, "note"));
    }

    [DedicatedPostgresFact]
    public async Task UnsafeInsertRuleTriggerAndCheckRemainBrowseOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var noKey = await fixture.TableAsync("note text");
        var check = await fixture.TableAsync("id integer PRIMARY KEY, note text CHECK(random()>=0)");
        var trigger = await fixture.TableAsync("id integer PRIMARY KEY, note text");
        await fixture.ExecuteAsync($"CREATE TRIGGER unsafe BEFORE INSERT ON {Q(trigger)} FOR EACH ROW EXECUTE FUNCTION smartsql_edit_test.noop_trigger()");
        var rule = await fixture.TableAsync("id integer PRIMARY KEY, note text");
        await fixture.ExecuteAsync($"CREATE RULE no_insert AS ON INSERT TO {Q(rule)} DO INSTEAD NOTHING");
        await using var browser = await EditorAsync();
        foreach (var table in new[] { noKey, check, trigger, rule })
        {
            var caps = await browser.GetInsertCapabilitiesAsync(table);
            Assert.False(caps.CanInsert);
            Assert.Null(caps.ConfirmationHandle);
            Assert.Empty((await browser.ReadPageAsync(table, 0, 10)).Rows);
        }
    }

    [DedicatedPostgresFact]
    public async Task CancellationDuringRealProtectiveLockWaitRollsBackWithoutRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id integer PRIMARY KEY, note text");
        await using var browser = await EditorAsync();
        var caps = await browser.GetInsertCapabilitiesAsync(table);
        await using var blocker = await fixture.InsertUncommittedAsync(table);
        using var cancellation = new CancellationTokenSource();
        var insert = browser.InsertRowAsync(Request(caps, Value("id", "1"), Value("note", "cancelled")), cancellation.Token);
        var waiting = false;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline && !insert.IsCompleted)
        {
            await fixture.ExecuteAsync("SELECT pg_catalog.pg_stat_clear_snapshot()", blocker);
            waiting = await fixture.ScalarAsync("SELECT count(*)::text FROM pg_catalog.pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE 'LOCK TABLE ONLY% IN SHARE ROW EXCLUSIVE MODE%'") != "0";
            if (waiting) break;
            await Task.Delay(20);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => insert);
        Assert.True(waiting, "Insertion must reach a real protective table-lock wait before cancellation.");
        await blocker.RollbackAsync();
        Assert.Equal("0", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
        caps = await browser.GetInsertCapabilitiesAsync(table);
        await browser.InsertRowAsync(Request(caps, Value("id", "1"), Value("note", "confirmed")));
        Assert.Equal("confirmed", await fixture.ScalarAsync($"SELECT note FROM {Q(table)}"));
    }

    [DedicatedPostgresFact]
    public async Task ProtectiveLockBlocksConcurrentUnsafeIndexUntilInsertionIsCancelled()
    {
        await using var fixture = await Fixture.CreateAsync();
        var table = await fixture.TableAsync("id bigserial PRIMARY KEY, note text");
        await using var browser = await EditorAsync();
        var caps = await browser.GetInsertCapabilitiesAsync(table);
        // ALTER SEQUENCE holds its relation lock without changing the effective increment.
        // The INSERT can acquire its table guard and validate metadata, then waits at nextval.
        await using var sequenceBlocker = await fixture.LockSequenceAsync(table);
        using var cancellation = new CancellationTokenSource();
        var insert = browser.InsertRowAsync(Request(caps, Value("note", "cancelled")), cancellation.Token);
        Task? ddl = null;
        var blockerReleased = false;
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            var guarded = false;
            while (DateTimeOffset.UtcNow < deadline && !insert.IsCompleted)
            {
                guarded = await fixture.ScalarAsync($"SELECT count(*)::text FROM pg_catalog.pg_locks WHERE relation='{Q(table)}'::regclass AND mode='ShareRowExclusiveLock' AND granted AND pid<>pg_backend_pid()") != "0";
                if (guarded) break;
                await Task.Delay(20);
            }
            Assert.True(guarded, "The provider must hold an actual ShareRowExclusiveLock, not merely request one.");
            ddl = fixture.CreateUnsafeIndexConcurrentlyAsync(table);
            var ddlWaiting = false;
            deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline && !ddl.IsCompleted)
            {
                await fixture.ExecuteAsync("SELECT pg_catalog.pg_stat_clear_snapshot()", sequenceBlocker);
                ddlWaiting = await fixture.ScalarAsync("SELECT count(*)::text FROM pg_catalog.pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE 'CREATE INDEX CONCURRENTLY%'") != "0";
                if (ddlWaiting) break;
                await Task.Delay(20);
            }
            Assert.True(ddlWaiting, "Concurrent unsafe index DDL must wait for the provider's protective lock.");
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => insert);
            await sequenceBlocker.RollbackAsync();
            blockerReleased = true;
            await ddl;
            Assert.Equal("0", await fixture.ScalarAsync($"SELECT count(*)::text FROM {Q(table)}"));
            Assert.False((await browser.GetInsertCapabilitiesAsync(table)).CanInsert);
        }
        finally
        {
            cancellation.Cancel();
            try { await insert; } catch (OperationCanceledException) { }
            if (!blockerReleased) await sequenceBlocker.RollbackAsync();
            if (ddl is not null) await ddl;
        }
    }

    private static DatabaseInsertValue Value(string name, string value) => new(name, DatabaseInsertValueMode.Value, value);
    private static DatabaseInsertValue Null(string name) => new(name, DatabaseInsertValueMode.Null);
    private static DatabaseInsertRequest Request(DatabaseInsertCapabilities caps, params DatabaseInsertValue[] values) =>
        new(caps.Table, caps.ConfirmationHandle!.Value, values);
    private static string? Cell(DatabaseInsertCapabilities caps, DatabaseInsertResult result, string name) =>
        result.Row.Values[caps.Columns.Single(c => c.Column.Name == name).Column.Ordinal];
    private static async Task ErrorAsync(string code, Task<DatabaseInsertResult> operation)
    {
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => operation);
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }
    private static Task<PostgresDatabaseBrowser> EditorAsync(string username = "smartsql_browser_editor") =>
        PostgresDatabaseBrowser.OpenForEditingAsync(Profile(username), string.Empty);
    private static ConnectionProfile Profile(string username = "smartsql_browser_editor")
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432")
            throw new InvalidOperationException("Insertion tests access only the dedicated loopback fixture on port 15432.");
        return new(Guid.NewGuid(), "Dedicated insertion fixture", DatabaseProviders.PostgreSql, "127.0.0.1", 15432,
            "smartsql_browser_test", username, ConnectionSslPolicy.Disable);
    }
    private static string Q(DatabaseTableRef table) => PostgresEditingSql.Qualified(table);

    private sealed class Fixture(NpgsqlConnection connection) : IAsyncDisposable
    {
        private readonly List<DatabaseTableRef> _tables = [];
        private readonly List<DatabaseTableRef> _sequences = [];
        public static async Task<Fixture> CreateAsync()
        {
            var settings = PostgresDatabaseBrowser.ConnectionSettings(Profile());
            settings.Options = "-c statement_timeout=15000 -c lock_timeout=3000";
            var connection = new NpgsqlConnection(settings.ConnectionString);
            try { await connection.OpenAsync(); return new Fixture(connection); }
            catch { await connection.DisposeAsync(); throw; }
        }
        public async Task<DatabaseTableRef> TableAsync(string definition)
        {
            var table = new DatabaseTableRef(Schema, "insert_" + Guid.NewGuid().ToString("N"), DatabaseObjectKind.Table);
            await ExecuteAsync($"CREATE TABLE {Q(table)} ({definition})");
            _tables.Add(table);
            return table;
        }
        public async Task<DatabaseTableRef> SequenceAsync()
        {
            var sequence = new DatabaseTableRef(Schema, "insert_sequence_" + Guid.NewGuid().ToString("N"), DatabaseObjectKind.Table);
            await ExecuteAsync($"CREATE SEQUENCE {Q(sequence)}");
            _sequences.Add(sequence);
            return sequence;
        }
        public async Task ExecuteAsync(string sql, NpgsqlTransaction? transaction = null)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        public async Task GrantProtectiveLockAsync(DatabaseTableRef table)
        {
            var version = await ScalarAsync("SELECT current_setting('server_version_num')");
            var privilege = int.Parse(version!, System.Globalization.CultureInfo.InvariantCulture) >= 170000 ? "MAINTAIN" : "DELETE";
            await ExecuteAsync($"GRANT {privilege} ON {Q(table)} TO smartsql_browser_reader");
        }
        public async Task<string?> ScalarAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? null : (string)value;
        }
        public async Task<NpgsqlTransaction> InsertUncommittedAsync(DatabaseTableRef table)
        {
            var transaction = await connection.BeginTransactionAsync();
            try
            {
                await ExecuteAsync($"INSERT INTO {Q(table)} VALUES (1,'blocker')", transaction);
                return transaction;
            }
            catch { await transaction.DisposeAsync(); throw; }
        }
        public async Task<NpgsqlTransaction> LockSequenceAsync(DatabaseTableRef table)
        {
            var sequence = await ScalarAsync($"SELECT pg_get_serial_sequence('{Q(table)}','id')");
            var transaction = await connection.BeginTransactionAsync();
            try
            {
                await ExecuteAsync($"ALTER SEQUENCE {sequence} INCREMENT BY 1", transaction);
                return transaction;
            }
            catch { await transaction.DisposeAsync(); throw; }
        }
        public async Task CreateUnsafeIndexConcurrentlyAsync(DatabaseTableRef table)
        {
            var settings = PostgresDatabaseBrowser.ConnectionSettings(Profile());
            settings.Options = "-c statement_timeout=15000 -c lock_timeout=10000";
            await using var ddlConnection = new NpgsqlConnection(settings.ConnectionString);
            await ddlConnection.OpenAsync();
            var indexName = PostgresBrowserSql.QuoteIdentifier("insert_ddl_" + Guid.NewGuid().ToString("N"));
            await using var command = new NpgsqlCommand($"CREATE INDEX CONCURRENTLY {indexName} ON {Q(table)} (smartsql_edit_test.unsafe_immutable(note))", ddlConnection);
            await command.ExecuteNonQueryAsync();
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var table in _tables.AsEnumerable().Reverse()) await ExecuteAsync($"DROP TABLE IF EXISTS {Q(table)}");
                foreach (var sequence in _sequences.AsEnumerable().Reverse()) await ExecuteAsync($"DROP SEQUENCE IF EXISTS {Q(sequence)}");
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
