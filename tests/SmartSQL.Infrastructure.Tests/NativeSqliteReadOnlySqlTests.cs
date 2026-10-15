using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class NativeSqliteReadOnlySqlTests
{
    static NativeSqliteReadOnlySqlTests() => SQLitePCL.Batteries_V2.Init();

    [Fact]
    public async Task ExactNativeValuesNamesStorageAndMainFileArePreserved()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE items(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO items VALUES(1,'Привет 🌍'); PRAGMA user_version=73");
        var hash = file.Hash();
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        var result = await sql.ExecuteAsync(new("SELECT id AS same, note AS same, NULL, '', x'00ff80', 9223372036854775807, 1.2345678901234567, CAST(x'610062' AS TEXT) FROM items"));
        Assert.Equal(new string?[] { "1", "Привет 🌍", null, "", "0x00FF80", "9223372036854775807", "1.2345678901234567", "a\0b" }, Assert.Single(result.Rows).Cells.Select(c => c.Value));
        Assert.Equal(new[] { "integer", "text", "null", "text", "blob", "integer", "real", "text" }, result.Rows[0].Cells.Select(c => c.StorageClass));
        Assert.Equal("same", result.Columns[0].Name);
        Assert.Equal("same", result.Columns[1].Name);
        Assert.Equal("INTEGER", result.Columns[0].NativeType);
        Assert.Null(result.Columns[2].NativeType);
        Assert.Equal(hash, file.Hash());
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task NativeParserHandlesLiteralsCommentsEmptySemicolonsCtesJoinsAndWindowFunctions()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE items(id INTEGER); INSERT INTO items VALUES(1),(2),(3); CREATE VIEW shown AS SELECT id FROM items");
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        var result = await sql.ExecuteAsync(new(";; /* header ; */ WITH n AS (SELECT id FROM shown) SELECT a.id, '; -- literal', row_number() OVER(ORDER BY a.id) FROM n a JOIN items b ON a.id=b.id ORDER BY a.id; ; -- tail", 2));
        Assert.Equal(new[] { "1", "2" }, result.Rows.Select(r => r.Cells[0].Value));
        Assert.True(result.Truncated);
        Assert.All(result.Rows, r => Assert.Equal("; -- literal", r.Cells[1].Value));
        var empty = await sql.ExecuteAsync(new("SELECT id FROM items WHERE 0"));
        Assert.Empty(empty.Rows);
        Assert.Single(empty.Columns);
        Assert.False(empty.Truncated);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; DELETE FROM items")]
    [InlineData("DELETE FROM items RETURNING id")]
    [InlineData("UPDATE items SET id=2 RETURNING id")]
    [InlineData("INSERT INTO items VALUES(2) RETURNING id")]
    [InlineData("CREATE TABLE forbidden(id INTEGER)")]
    [InlineData("DROP TABLE items")]
    [InlineData("WITH n AS (SELECT 1) DELETE FROM items RETURNING id")]
    [InlineData("PRAGMA query_only=OFF")]
    [InlineData("PRAGMA user_version")]
    [InlineData("SELECT * FROM pragma_table_list")]
    [InlineData("ATTACH DATABASE ':memory:' AS other")]
    [InlineData("DETACH DATABASE main")]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("SAVEPOINT sample")]
    [InlineData("VACUUM")]
    [InlineData("SELECT load_extension('forbidden')")]
    [InlineData("SELECT randomblob(100)")]
    [InlineData("SELECT $value")]
    public async Task WritesControlMultipleCommandsVirtualPragmasUnknownFunctionsAndParametersAreRejected(string text)
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE items(id INTEGER); INSERT INTO items VALUES(1)");
        var hash = file.Hash();
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new(text)));
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(text, failure.ToString());
        Assert.Equal(hash, file.Hash());
        Assert.Equal("1", Assert.Single((await sql.ExecuteAsync(new("SELECT id FROM items"))).Rows).Cells[0].Value);
    }

    [Fact]
    public async Task VirtualAndShadowTablesStayInaccessible()
    {
        using var file = new Fixture();
        file.Execute("CREATE VIRTUAL TABLE documents USING fts5(body); INSERT INTO documents VALUES('example')");
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        foreach (var table in new[] { "documents", "documents_data", "documents_idx", "documents_content", "documents_docsize", "documents_config" })
        {
            await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("SELECT * FROM " + table)));
            await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("SELECT count(*) FROM " + table)));
        }
        await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("SELECT count(*) FROM temp.sqlite_temp_schema")));
        await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("WITH n AS (SELECT count(*) FROM documents_data) SELECT count(*) FROM n")));
    }

    [Fact]
    public async Task InvalidUtf8AndOversizeValuesFailWithoutReplacementOrPartialResults()
    {
        using var file = new Fixture();
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        var invalid = await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("SELECT CAST(x'FF' AS TEXT)")));
        Assert.Equal("query_text_invalid", invalid.Code);
        var large = await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("SELECT printf('%.*c',1000001,'x')")));
        Assert.Equal("cell_too_large", large.Code);
        Assert.Equal("42", Assert.Single((await sql.ExecuteAsync(new("VALUES(42)"))).Rows).Cells[0].Value);
    }

    [Fact]
    public async Task EntireResultLimitFailsWithoutReturningATruncatedPartialPage()
    {
        using var file = new Fixture();
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i+1 FROM n WHERE i<17) SELECT i, printf('%.*c',1000000,'x') FROM n", 17)));
        Assert.Equal("query_result_too_large", failure.Code);
        Assert.Equal("1", Assert.Single((await sql.ExecuteAsync(new("SELECT 1"))).Rows).Cells[0].Value);
    }

    [Fact]
    public async Task ColumnlessReadsRequireAConfirmedMainObjectAndNeverAdmitUnknownCteNames()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE items(id INTEGER); INSERT INTO items VALUES(1),(2),(3)");
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        Assert.Equal("3", Assert.Single((await sql.ExecuteAsync(new("SELECT count(*) FROM items"))).Rows).Cells[0].Value);
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => sql.ExecuteAsync(new("WITH n(i) AS (VALUES(1),(2),(3)) SELECT count(*) FROM n")));
        Assert.Equal("query_policy_denied", failure.Code);
        Assert.Equal("3", Assert.Single((await sql.ExecuteAsync(new("WITH n AS (SELECT id FROM items) SELECT count(id) FROM n"))).Rows).Cells[0].Value);
    }

    [Fact]
    public async Task Utf16IsExplicitlyUnsupportedOnlyForManualSql()
    {
        using var file = new Fixture();
        file.Execute("PRAGMA encoding='UTF-16le'; CREATE TABLE items(note TEXT); INSERT INTO items VALUES('Привет 🌍')");
        var hash = file.Hash();
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path));
        Assert.Equal("query_encoding_unsupported", failure.Code);
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        Assert.Equal("Привет 🌍", Assert.Single((await browser.ReadPageAsync(Assert.Single(await browser.GetTablesAsync()), 0, 1)).Rows).Values[0]);
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task CancellationInterruptsNativeWorkAndNextQueryStillWorks()
    {
        using var file = new Fixture();
        await using var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sql.ExecuteAsync(new(LongQuery), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.Equal("1", Assert.Single((await sql.ExecuteAsync(new("SELECT 1"))).Rows).Cells[0].Value);
    }

    [Fact]
    public async Task DisposalCancelsRunningAndQueuedOperationsThenRejectsWork()
    {
        using var file = new Fixture();
        var sql = await NativeSqliteReadOnlySqlExecutor.OpenAsync(file.Path);
        var running = sql.ExecuteAsync(new(LongQuery));
        var queued = sql.ExecuteAsync(new("SELECT 2"));
        var disposal = sql.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(8)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(8)));
        await disposal.WaitAsync(TimeSpan.FromSeconds(8));
        await sql.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sql.ExecuteAsync(new("SELECT 3")));
    }

    private const string LongQuery = "WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i+1 FROM n WHERE i<1000000000) SELECT sum(i) FROM n";
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(TestPaths.Root, "native-sql", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public Fixture() { Directory.CreateDirectory(_directory); Path = System.IO.Path.Combine(_directory, "ordinary.db"); Execute("PRAGMA user_version=0"); }
        public void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
        public string Hash()
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
