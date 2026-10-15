using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>Ordinary synthetic databases under TestPaths only; no managed migrations or user files.</summary>
public sealed class NativeSqliteEditingTests
{
    static NativeSqliteEditingTests() => SQLitePCL.Batteries_V2.Init();

    [Fact]
    public async Task ReadOnlyRemainsDefaultAndOptInDoesNotAddInsertOrDeleteAuthority()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'before'); PRAGMA user_version=77");
        var hash = file.Hash();
        await using (var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path))
        {
            Assert.False(((IDatabaseEditor)browser).EditingEnabled);
            var table = await Table(browser);
            Assert.False((await ((IDatabaseEditor)browser).GetEditCapabilitiesAsync(table)).CanUpdate);
            Assert.Null(Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows).EditHandle);
            await Error("edit_not_allowed", ((IDatabaseEditor)browser).UpdateCellAsync(new(table, Guid.NewGuid(), "note", "after")));
        }
        Assert.Equal(hash, file.Hash());
        await using var editable = await Open(file);
        Assert.True(((IDatabaseEditor)editable).EditingEnabled);
        Assert.False((object)editable is IDatabaseRowCreator);
        Assert.False((object)editable is IDatabaseRowDeleter);
        Assert.Equal(77L, file.Scalar("PRAGMA user_version"));
        Assert.Equal(new[] { "data" }, (await editable.GetTablesAsync()).Select(table => table.Name));
        Assert.Empty(file.Backups);
    }

    [Theory]
    [InlineData("id INTEGER PRIMARY KEY,note TEXT", "(1,'first'),(2,'second')", false)]
    [InlineData("id TEXT NOT NULL PRIMARY KEY,note TEXT", "('1','first'),('2','second')", false)]
    [InlineData("id INTEGER NOT NULL,part TEXT NOT NULL,note TEXT,PRIMARY KEY(id,part)", "(1,'a','first'),(1,'b','second')", false)]
    [InlineData("id INTEGER,part TEXT,note TEXT,PRIMARY KEY(id,part)", "(1,'a','first'),(1,'b','second')", true)]
    public async Task ConfirmedDeclaredKeysAddressExactlyOneRow(string definition, string rows, bool withoutRowId)
    {
        using var file = new Fixture();
        file.Execute($"CREATE TABLE data({definition}){(withoutRowId ? " WITHOUT ROWID" : "")}; INSERT INTO data VALUES {rows}");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var capabilities = await browser.GetEditCapabilitiesAsync(table);
        Assert.True(capabilities.CanUpdate);
        Assert.True(capabilities.Columns.Single(column => column.Name == "note").CanEdit);
        var page = await browser.ReadPageAsync(table, 0, 10);
        var first = page.Rows[0];
        Assert.NotNull(first.EditHandle);
        await browser.UpdateCellAsync(new(table, first.EditHandle!.Value, "note", "  changed 🌍  "));
        Assert.Equal(1L, file.Scalar("SELECT count(*) FROM data WHERE note='  changed 🌍  '"));
        Assert.Equal(1L, file.Scalar("SELECT count(*) FROM data WHERE note='second'"));
        Assert.Single(file.Backups);
    }

    [Theory]
    [InlineData("CREATE TABLE data(note TEXT)", "data")]
    [InlineData("CREATE TABLE data(id TEXT PRIMARY KEY,note TEXT)", "data")]
    [InlineData("CREATE TABLE data(id INTEGER PRIMARY KEY DESC,note TEXT)", "data")]
    [InlineData("CREATE TABLE data(id REAL NOT NULL PRIMARY KEY,note TEXT)", "data")]
    [InlineData("CREATE TABLE base(id INTEGER PRIMARY KEY,note TEXT); CREATE VIEW data AS SELECT * FROM base", "data")]
    [InlineData("CREATE VIRTUAL TABLE data USING fts5(note)", "data")]
    [InlineData("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); CREATE TABLE audit(note TEXT); CREATE TRIGGER data_audit AFTER UPDATE ON data BEGIN INSERT INTO audit VALUES(new.note); END", "data")]
    [InlineData("CREATE TABLE parent(id INTEGER PRIMARY KEY); CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT,parent_id INTEGER REFERENCES parent(id))", "data")]
    [InlineData("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); CREATE TABLE child(id INTEGER PRIMARY KEY,parent_id INTEGER REFERENCES data(id))", "data")]
    [InlineData("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT CHECK(length(note)<10))", "data")]
    [InlineData("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT,calculated TEXT GENERATED ALWAYS AS(note||'!') STORED)", "data")]
    [InlineData("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); CREATE INDEX expression_index ON data(lower(note))", "data")]
    [InlineData("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); CREATE INDEX partial_index ON data(note) WHERE note IS NOT NULL", "data")]
    public async Task UnsafeOrUnsupportedStructuresNeverOfferCellWrites(string sql, string name)
    {
        using var file = new Fixture();
        file.Execute(sql);
        var hash = file.Hash();
        await using var browser = await Open(file);
        var table = await Table(browser, name);
        try
        {
            var capabilities = await browser.GetEditCapabilitiesAsync(table);
            Assert.False(capabilities.CanUpdate);
            Assert.NotEmpty(capabilities.Diagnostics);
        }
        catch (DatabaseBrowserException error)
        {
            Assert.Equal("unsupported_object", error.Code);
            Assert.Null(error.InnerException);
        }
        Assert.Equal(hash, file.Hash());
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task CustomCollationIndexIsReadOnlyEvenWhenRegisteredForFixtureCreation()
    {
        using var file = new Fixture();
        using (var connection = file.Connect())
        {
            connection.CreateCollation("CUSTOM_ORDER", (left, right) => StringComparer.Ordinal.Compare(left, right));
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); CREATE INDEX custom_index ON data(note COLLATE CUSTOM_ORDER); INSERT INTO data VALUES(1,'before')";
            command.ExecuteNonQuery();
        }
        var hash = file.Hash();
        await using var browser = await Open(file);
        Assert.False((await browser.GetEditCapabilitiesAsync(await Table(browser))).CanUpdate);
        Assert.Equal(hash, file.Hash());
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task PrimaryUniqueAndUnsupportedFieldsAreProtectedWithoutBlockingOrdinaryText()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,unique_note TEXT UNIQUE,note TEXT,payload BLOB,untyped); INSERT INTO data VALUES(1,'unique','before',x'00ff80',23)");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var capabilities = await browser.GetEditCapabilitiesAsync(table);
        Assert.True(capabilities.CanUpdate);
        foreach (var name in new[] { "id", "unique_note", "payload", "untyped" })
        {
            Assert.False(capabilities.Columns.Single(column => column.Name == name).CanEdit);
            var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
            await Error("edit_not_allowed", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, name, "change")));
        }
        Assert.Empty(file.Backups);
        await Update(browser, table, "note", "allowed");
        Assert.Equal("allowed", file.Scalar("SELECT note FROM data"));
        Assert.Equal("00FF80", file.Scalar("SELECT hex(payload) FROM data"));
        Assert.Equal(23L, file.Scalar("SELECT untyped FROM data"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("'\"; UPDATE data SET note='all'; -- 🌍\r\n")]
    public async Task TextValuesAreExactAndNullIsSeparateFromEmptyAndWhitespace(string? value)
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'before'),(2,'untouched')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        await Update(browser, table, "note", value);
        Assert.Equal((object?)value ?? DBNull.Value, file.Scalar("SELECT note FROM data WHERE id=1"));
        Assert.Equal("untouched", file.Scalar("SELECT note FROM data WHERE id=2"));
        Assert.Equal("before", Fixture.ReadBackup(file.Backups.Single(), "SELECT note FROM data WHERE id=1"));
    }

    [Theory]
    [InlineData("INTEGER", "9223372036854775807", "integer", "9223372036854775807")]
    [InlineData("INTEGER", "-9223372036854775808", "integer", "-9223372036854775808")]
    [InlineData("NUMERIC", "9223372036854775807", "integer", "9223372036854775807")]
    [InlineData("NUMERIC", "9007199254740993", "integer", "9007199254740993")]
    [InlineData("NUMERIC", "1.2345678901234567", "real", "1.2345678901234567")]
    [InlineData("REAL", "0.1", "real", "0.1")]
    [InlineData("DATE", "2024-02-29", "text", "2024-02-29")]
    [InlineData("BOOLEAN", "true", "integer", "1")]
    [InlineData("BOOLEAN", "false", "integer", "0")]
    public async Task NativeInputsRetainPromisedStorageAndExactRoundTrip(string declaration, string input, string storage, string shown)
    {
        using var file = new Fixture();
        file.Execute($"CREATE TABLE data(id INTEGER PRIMARY KEY,value {declaration}); INSERT INTO data VALUES(1,NULL)");
        await using var browser = await Open(file);
        var table = await Table(browser);
        await Update(browser, table, "value", input);
        Assert.Equal(storage, file.Scalar("SELECT typeof(value) FROM data"));
        Assert.Equal(shown, Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows).Values[1]);
    }

    [Theory]
    [InlineData("INTEGER", "9223372036854775808")]
    [InlineData("INTEGER", "1.5")]
    [InlineData("NUMERIC", "1.00000000000000000000000000001")]
    [InlineData("NUMERIC", "1e9999")]
    [InlineData("REAL", "9223372036854775807")]
    [InlineData("REAL", "9007199254740993")]
    [InlineData("DATE", "2025-02-29")]
    [InlineData("BOOLEAN", "2")]
    public async Task InvalidOrLossyInputDoesNotOverwriteOrCreateBackup(string declaration, string input)
    {
        using var file = new Fixture();
        file.Execute($"CREATE TABLE data(id INTEGER PRIMARY KEY,value {declaration}); INSERT INTO data VALUES(1,NULL)");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "value", input)));
        Assert.Contains(error.Code, new[] { "edit_value_invalid", "edit_precision_loss" });
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(input, error.Message);
        Assert.Equal(DBNull.Value, file.Scalar("SELECT value FROM data"));
        Assert.Empty(file.Backups);
        await Error("edit_handle_expired", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "value", null)));
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-16le")]
    [InlineData("UTF-16be")]
    public async Task UnicodeEncodingAndUnmodifiedBlobBytesSurviveCellUpdate(string encoding)
    {
        using var file = new Fixture();
        file.Execute($"PRAGMA encoding='{encoding}'; CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT,payload BLOB); INSERT INTO data VALUES(1,'Привет 🌍',x'00ff80c32800')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        await Update(browser, table, "note", "  Другой 🌍 текст  ");
        Assert.Equal(encoding, file.Scalar("PRAGMA encoding"));
        Assert.Equal("  Другой 🌍 текст  ", file.Scalar("SELECT note FROM data"));
        Assert.Equal("00FF80C32800", file.Scalar("SELECT hex(payload) FROM data"));
        Assert.Equal("Привет 🌍", Fixture.ReadBackup(file.Backups.Single(), "SELECT note FROM data"));
        Assert.Equal("00FF80C32800", Fixture.ReadBackup(file.Backups.Single(), "SELECT hex(payload) FROM data"));
    }

    [Fact]
    public async Task IncompatibleStorageOutsideTheSelectedRowBlocksTheTargetColumnWithoutCoercion()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,value INTEGER,note TEXT); INSERT INTO data VALUES(1,1,'selected'),(2,2,'other')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var selected = Assert.Single((await browser.ReadPageAsync(table, 0, 1)).Rows);
        file.Execute("UPDATE data SET value='incompatible integer storage' WHERE id=2");
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.UpdateCellAsync(new(table, selected.EditHandle!.Value, "value", "3")));
        Assert.Contains(error.Code, new[] { "edit_not_allowed", "edit_schema_changed", "edit_value_invalid" });
        Assert.Equal(1L, file.Scalar("SELECT value FROM data WHERE id=1"));
        Assert.Equal("incompatible integer storage", file.Scalar("SELECT value FROM data WHERE id=2"));
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task NullDoesNotBypassDeclaredNotNullConstraint()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT NOT NULL); INSERT INTO data VALUES(1,'before')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        await Error("edit_value_invalid", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", null)));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task ChangedOriginalRowAndSchemaAreNotOverwritten()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT,other TEXT); INSERT INTO data VALUES(1,'before','original')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        file.Execute("UPDATE data SET other='outside change' WHERE id=1");
        await Error("edit_conflict", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "lost update")));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Equal("outside change", file.Scalar("SELECT other FROM data"));
        row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        file.Execute("ALTER TABLE data ADD COLUMN added TEXT");
        await Error("edit_schema_changed", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "after schema change")));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task UnchangedDisplayedTextButChangedRawStorageStillConflicts()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT,raw); INSERT INTO data VALUES(1,'before',1)");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        file.Execute("UPDATE data SET raw=CAST(1 AS REAL)");
        await Error("edit_conflict", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "not saved")));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Equal("real", file.Scalar("SELECT typeof(raw) FROM data"));
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task HandlesArePrivateToBrowserTableAndCurrentPageAndConsumedAfterSuccess()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'first'),(2,'second'); CREATE TABLE other(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO other VALUES(1,'other')");
        await using var browser = await Open(file);
        await using var otherBrowser = await Open(file);
        var table = await Table(browser);
        var otherTable = new DatabaseTableRef("main", "other", DatabaseObjectKind.Table);
        var first = Assert.Single((await browser.ReadPageAsync(table, 0, 1)).Rows);
        await Error("edit_handle_expired", otherBrowser.UpdateCellAsync(new(table, first.EditHandle!.Value, "note", "wrong browser")));
        await Error("edit_handle_expired", browser.UpdateCellAsync(new(otherTable, first.EditHandle.Value, "note", "wrong table")));
        first = Assert.Single((await browser.ReadPageAsync(table, 0, 1)).Rows);
        await browser.ReadPageAsync(table, 1, 1);
        await Error("edit_handle_expired", browser.UpdateCellAsync(new(table, first.EditHandle!.Value, "note", "old page")));
        first = Assert.Single((await browser.ReadPageAsync(table, 0, 1)).Rows);
        await browser.UpdateCellAsync(new(table, first.EditHandle!.Value, "note", "saved"));
        await Error("edit_handle_expired", browser.UpdateCellAsync(new(table, first.EditHandle.Value, "note", "reused")));
        Assert.Equal("saved", file.Scalar("SELECT note FROM data WHERE id=1"));
        Assert.Equal("second", file.Scalar("SELECT note FROM data WHERE id=2"));
        Assert.Equal("other", file.Scalar("SELECT note FROM other WHERE id=1"));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("columns")]
    [InlineData("edit-capabilities")]
    [InlineData("query-capabilities")]
    public async Task MetadataReloadInvalidatesPreviousPageHandles(string operation)
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'before')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        switch (operation)
        {
            case "catalog": await browser.GetTablesAsync(); break;
            case "columns": await browser.GetColumnsAsync(table); break;
            case "edit-capabilities": await browser.GetEditCapabilitiesAsync(table); break;
            default: await browser.GetQueryCapabilitiesAsync(table); break;
        }
        await Error("edit_handle_expired", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "after reload")));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task PrecancelledWriteAndDisposedSessionNeverChangeData()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'before')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "cancelled"), cancellation.Token));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Empty(file.Backups);
        await browser.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "disposed")));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
    }

    [Fact]
    public async Task CancellationWhileWaitingForAnotherWriterNeverUpdatesOrCreatesBackup()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'before')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        using var writer = file.Connect();
        using var writerTransaction = writer.BeginTransaction(deferred: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        try
        {
            var update = browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "cancelled while locked"), cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update.WaitAsync(TimeSpan.FromSeconds(8)));
        }
        finally
        {
            writerTransaction.Rollback();
        }
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Empty(file.Backups);
    }

    [Fact]
    public async Task FailedMandatoryBackupPreventsUpdateAndNeverOverwritesTheBlockingFile()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'before')");
        await using var browser = await Open(file);
        var table = await Table(browser);
        var row = Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows);
        if (Directory.Exists(file.BackupDirectory)) Directory.Delete(file.BackupDirectory);
        File.WriteAllText(file.BackupDirectory, "backup destination blocked");
        await Error("edit_backup_failed", browser.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "must not save")));
        Assert.Equal("before", file.Scalar("SELECT note FROM data"));
        Assert.Equal("backup destination blocked", File.ReadAllText(file.BackupDirectory));
    }

    [Fact]
    public async Task EachMandatoryBackupIsDistinctReadablePrewriteDatabaseWithoutManagedMigration()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'initial'); PRAGMA user_version=77; PRAGMA application_id=123456");
        var schema = file.Scalar("SELECT group_concat(sql,';') FROM sqlite_schema WHERE sql IS NOT NULL");
        await using var browser = await Open(file);
        var table = await Table(browser);
        await Update(browser, table, "note", "first update");
        var firstBackup = Assert.Single(file.Backups);
        var firstHash = Fixture.HashFile(firstBackup);
        await Update(browser, table, "note", "second update");
        Assert.Equal(2, file.Backups.Length);
        var secondBackup = Assert.Single(file.Backups, path => path != firstBackup);
        Assert.Equal("initial", Fixture.ReadBackup(firstBackup, "SELECT note FROM data"));
        Assert.Equal("first update", Fixture.ReadBackup(secondBackup, "SELECT note FROM data"));
        Assert.Equal(firstHash, Fixture.HashFile(firstBackup));
        foreach (var backup in file.Backups)
        {
            Assert.Equal("ok", Fixture.ReadBackup(backup, "PRAGMA integrity_check"));
            Assert.Equal(77L, Fixture.ReadBackup(backup, "PRAGMA user_version"));
            Assert.Equal(123456L, Fixture.ReadBackup(backup, "PRAGMA application_id"));
            Assert.Equal(schema, Fixture.ReadBackup(backup, "SELECT group_concat(sql,';') FROM sqlite_schema WHERE sql IS NOT NULL"));
        }
        Assert.Equal(schema, file.Scalar("SELECT group_concat(sql,';') FROM sqlite_schema WHERE sql IS NOT NULL"));
        Assert.Equal(77L, file.Scalar("PRAGMA user_version"));
        Assert.Equal(123456L, file.Scalar("PRAGMA application_id"));
        Assert.Equal(new[] { "data" }, (await browser.GetTablesAsync()).Select(item => item.Name));
    }

    [Fact]
    public async Task BackupIncludesCommittedLiveWalContentAndIsSelfContainedWithoutWalSidecars()
    {
        using var file = new Fixture();
        using var writer = file.Connect();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'committed WAL'),(2,'other WAL')";
            command.ExecuteNonQuery();
        }
        Assert.True(File.Exists(file.Path + "-wal"));
        await using var browser = await Open(file);
        var table = await Table(browser);
        await Update(browser, table, "note", "updated WAL");
        var backup = Assert.Single(file.Backups);
        Assert.Equal("committed WAL", Fixture.ReadBackup(backup, "SELECT note FROM data WHERE id=1"));
        Assert.Equal("other WAL", Fixture.ReadBackup(backup, "SELECT note FROM data WHERE id=2"));
        Assert.Equal("ok", Fixture.ReadBackup(backup, "PRAGMA integrity_check"));
        Assert.Equal("updated WAL", file.Scalar("SELECT note FROM data WHERE id=1"));
        var restored = System.IO.Path.Combine(file.DirectoryPath, "restored-only-main.db");
        File.Copy(backup, restored);
        Assert.Equal("committed WAL", Fixture.ReadBackup(restored, "SELECT note FROM data WHERE id=1"));
        Assert.Equal("ok", Fixture.ReadBackup(restored, "PRAGMA integrity_check"));
    }

    private static Task<NativeSqliteDatabaseBrowser> Open(Fixture file) => NativeSqliteDatabaseBrowser.OpenEditableAsync(file.Path, file.BackupDirectory);
    private static async Task<DatabaseTableRef> Table(IDatabaseBrowser browser, string name = "data") => Assert.Single(await browser.GetTablesAsync(), table => table.Name == name);
    private static async Task<DatabaseUpdateResult> Update(IDatabaseEditor browser, DatabaseTableRef table, string column, string? value)
    {
        var row = (await browser.ReadPageAsync(table, 0, 100)).Rows[0];
        Assert.NotNull(row.EditHandle);
        return await browser.UpdateCellAsync(new(table, row.EditHandle!.Value, column, value));
    }
    private static async Task Error(string code, Task operation)
    {
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => operation);
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }

    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = System.IO.Path.Combine(TestPaths.Root, "native-sqlite-editing", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(DirectoryPath, "ordinary.db");
        public string BackupDirectory => System.IO.Path.Combine(DirectoryPath, "backups");
        public string[] Backups => Directory.Exists(BackupDirectory) ? Directory.GetFiles(BackupDirectory, "*.db", SearchOption.AllDirectories) : [];
        public Fixture()
        {
            Assert.Equal("D:\\", System.IO.Path.GetPathRoot(DirectoryPath), ignoreCase: true);
            Directory.CreateDirectory(DirectoryPath);
        }
        public SqliteConnection Connect()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false, DefaultTimeout = 3 }.ToString());
            try { connection.Open(); return connection; }
            catch { connection.Dispose(); throw; }
        }
        public void Execute(string sql)
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        public object? Scalar(string sql)
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }
        public string Hash() => HashFile(Path);
        public static string HashFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        public static object? ReadBackup(string path, string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
