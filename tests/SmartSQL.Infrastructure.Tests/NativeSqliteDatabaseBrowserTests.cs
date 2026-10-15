using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class NativeSqliteDatabaseBrowserTests
{
    static NativeSqliteDatabaseBrowserTests() => SQLitePCL.Batteries_V2.Init();

    [Fact]
    public async Task OrdinaryCatalogCompositeKeysViewsAndGeneratedColumnsNeedNoManagedMetadata()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE \"таблица \"\" ;--\" (b TEXT, a INTEGER, note TEXT, calculated TEXT GENERATED ALWAYS AS (note || '!') STORED, PRIMARY KEY(a,b)) WITHOUT ROWID; INSERT INTO \"таблица \"\" ;--\" VALUES ('x',1,'🌍'); CREATE VIEW shown AS SELECT note FROM \"таблица \"\" ;--\"; CREATE TABLE nullable_key(k TEXT PRIMARY KEY,note TEXT); CREATE TABLE rowid_key(k INTEGER PRIMARY KEY,note TEXT); CREATE TABLE descending_key(k INTEGER PRIMARY KEY DESC,note TEXT); PRAGMA user_version=77");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var tables = await browser.GetTablesAsync();
        Assert.Equal(5, tables.Count);
        Assert.DoesNotContain(tables, t => t.Name.StartsWith("smartsql_"));
        var table = tables.Single(t => t.Name.StartsWith("таблица"));
        var columns = await browser.GetColumnsAsync(table);
        Assert.Equal(new[] { "b", "a", "note", "calculated" }, columns.Select(c => c.Name));
        Assert.Equal(2, columns[0].PrimaryKeyOrder);
        Assert.Equal(1, columns[1].PrimaryKeyOrder);
        Assert.False(columns[0].IsNullable);
        Assert.Equal("🌍!", Assert.Single((await browser.ReadPageAsync(table, 0, 10)).Rows).Values[3]);
        Assert.True(Assert.Single(await browser.GetColumnsAsync(tables.Single(t => t.Name == "nullable_key")), c => c.Name == "k").IsNullable);
        Assert.False(Assert.Single(await browser.GetColumnsAsync(tables.Single(t => t.Name == "rowid_key")), c => c.Name == "k").IsNullable);
        Assert.True(Assert.Single(await browser.GetColumnsAsync(tables.Single(t => t.Name == "descending_key")), c => c.Name == "k").IsNullable);
        var view = tables.Single(t => t.Name == "shown");
        Assert.Equal(DatabaseObjectKind.View, view.Kind);
        Assert.Equal("🌍", Assert.Single((await browser.ReadPageAsync(view, 0, 10)).Rows).Values[0]);
        Assert.Equal(hash, file.Hash());
        Assert.Equal(77L, file.Scalar("PRAGMA user_version"));
    }

    [Fact]
    public async Task NativeStorageValuesPreserveNullEmptyWhitespaceBinaryIntegerAndDouble()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE mixed(id INTEGER PRIMARY KEY,value); INSERT INTO mixed VALUES(1,NULL),(2,''),(3,'  🌍  '),(4,x'00ff80'),(5,9223372036854775807),(6,1.2345678901234567),(7,CAST(x'610062' AS TEXT))");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = Assert.Single(await browser.GetTablesAsync());
        var page = await browser.ReadPageAsync(table, 0, 10);
        Assert.Equal(new string?[] { null, "", "  🌍  ", "0x00FF80", "9223372036854775807", "1.2345678901234567", "a\0b" }, page.Rows.Select(r => r.Values[1]));
        Assert.All(page.Rows, r => Assert.Null(r.EditHandle));
        Assert.Equal(string.Empty, page.Columns[1].NativeType);
        Assert.Contains(page.Diagnostics, d => d.Code == "sqlite_storage_classes");
        var descending = await browser.ReadPageAsync(table, 0, 10, new DatabaseBrowseQuery(SortColumn: "value", Descending: true));
        Assert.Null(descending.Rows[0].Values[1]);
        Assert.Equal(new string?[] { null, "0x00FF80", "a\0b", "  🌍  ", "", "9223372036854775807", "1.2345678901234567" },
            descending.Rows.Select(r => r.Values[1]));
    }

    [Fact]
    public async Task PagingSearchAndNativeSortApplyToWholeTableAndIdentifiersStayLiteral()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE rows(id INTEGER PRIMARY KEY, note TEXT); WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i+1 FROM n WHERE i<205) INSERT INTO rows SELECT i, CASE WHEN i%2=0 THEN 'MATCH_%' ELSE 'other' END FROM n");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = Assert.Single(await browser.GetTablesAsync());
        var first = await browser.ReadPageAsync(table, 0, 100);
        var second = await browser.ReadPageAsync(table, 100, 100);
        var last = await browser.ReadPageAsync(table, 200, 100);
        Assert.True(first.HasMore);
        Assert.True(second.HasMore);
        Assert.False(last.HasMore);
        Assert.Equal(5, last.Rows.Count);
        Assert.Equal(Enumerable.Range(1,205).Select(i => i.ToString()), first.Rows.Concat(second.Rows).Concat(last.Rows).Select(r => r.Values[0]));
        var filtered = await browser.ReadPageAsync(table, 100, 10, new DatabaseBrowseQuery("match_%"));
        Assert.Equal(2, filtered.Rows.Count);
        Assert.Equal("202", filtered.Rows[0].Values[0]);
        var sorted = await browser.ReadPageAsync(table, 0, 3, new DatabaseBrowseQuery(SortColumn:"id"));
        Assert.Equal(new[] { "1", "2", "3" }, sorted.Rows.Select(r => r.Values[0]));
        Assert.Contains(sorted.Diagnostics, d => d.Code == "selected_native_sort");
        Assert.Empty((await browser.ReadPageAsync(table, 0, 10, new DatabaseBrowseQuery("'; DELETE FROM rows;--"))).Rows);
        var injected = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 10, new DatabaseBrowseQuery(SortColumn:"id\"; DELETE FROM rows;--")));
        Assert.Equal("sort_column_unavailable", injected.Code);
        await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table with { Name="rows\"; DELETE FROM rows;--" }, 0, 10));
        Assert.Equal(205L, file.Scalar("SELECT count(*) FROM rows"));
    }

    [Fact]
    public async Task MissingCorruptVirtualAndInvalidTextAreRejectedExplicitly()
    {
        using var file = new Fixture();
        var missing = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file.Path)!,"missing.db");
        await Assert.ThrowsAsync<DatabaseBrowserException>(() => NativeSqliteDatabaseBrowser.OpenAsync(missing));
        Assert.False(File.Exists(missing));
        await Assert.ThrowsAsync<ArgumentException>(() => NativeSqliteDatabaseBrowser.OpenAsync("relative.db"));
        file.Execute("CREATE TABLE bad(value); INSERT INTO bad VALUES(CAST(x'ff' AS TEXT)); CREATE VIRTUAL TABLE fulltext USING fts5(value)");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var tables = await browser.GetTablesAsync();
        var text = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(tables.Single(t => t.Name=="bad"),0,10));
        Assert.Equal("unsupported_text_encoding",text.Code);
        var unsupported = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetColumnsAsync(tables.Single(t=>t.Name=="fulltext")));
        Assert.Equal("unsupported_object",unsupported.Code);
        var corrupt = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file.Path)!,"corrupt.db");
        File.WriteAllText(corrupt,"not SQLite");
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(()=>NativeSqliteDatabaseBrowser.OpenAsync(corrupt));
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task BoundsCancellationDisposalAndKeylessDiagnosticAreHonest()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE values_table(value TEXT); INSERT INTO values_table VALUES('same'),('same')");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = Assert.Single(await browser.GetTablesAsync());
        Assert.Contains((await browser.ReadPageAsync(table,0,1)).Diagnostics,d=>d.Code=="unstable_pagination");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>browser.ReadPageAsync(table,-1,10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>browser.ReadPageAsync(table,0,501));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>browser.ReadPageAsync(table,0,10,cancellation.Token));
        file.Execute("DELETE FROM values_table; INSERT INTO values_table VALUES(printf('%.*c',1000001,'x'))");
        var large = await Assert.ThrowsAsync<DatabaseBrowserException>(()=>browser.ReadPageAsync(table,0,10));
        Assert.Equal("cell_too_large",large.Code);
        await browser.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>browser.GetTablesAsync());
    }

    [Fact]
    public async Task Utf16NativeFileUsesItsDeclaredEncodingWithoutReplacingText()
    {
        using var file = new Fixture();
        file.Execute("PRAGMA encoding='UTF-16le'; CREATE TABLE unicode(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO unicode VALUES(1,'Привет 🌍')");
        Assert.Equal("UTF-16le",file.Scalar("PRAGMA encoding"));
        var hash=file.Hash();
        await using var browser=await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var page=await browser.ReadPageAsync(Assert.Single(await browser.GetTablesAsync()),0,10);
        Assert.Equal("Привет 🌍",Assert.Single(page.Rows).Values[1]);
        Assert.Equal(hash,file.Hash());
    }

    [Fact]
    public async Task LiveWalRowsAreVisibleWithoutCheckpointOrMainFileChanges()
    {
        using var file = new Fixture();
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=file.Path,Pooling=false }.ToString());
        writer.Open();
        using var command = writer.CreateCommand();
        command.CommandText="PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE wal_rows(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO wal_rows VALUES(1,'live WAL')";
        command.ExecuteNonQuery();
        var mainHash=file.Hash();
        var walHash=Fixture.HashFile(file.Path+"-wal");
        await using var browser=await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var page=await browser.ReadPageAsync(Assert.Single(await browser.GetTablesAsync()),0,10);
        Assert.Equal("live WAL",Assert.Single(page.Rows).Values[1]);
        Assert.Contains(page.Diagnostics,d=>d.Code=="sqlite_readonly_wal");
        Assert.Equal(mainHash,file.Hash());
        Assert.Equal(walHash,Fixture.HashFile(file.Path+"-wal"));
        Assert.StartsWith(TestPaths.Root,file.Path);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory=System.IO.Path.Combine(TestPaths.Root,"native-sqlite",Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public Fixture() { Directory.CreateDirectory(_directory); Path=System.IO.Path.Combine(_directory,"ordinary.db"); Execute("PRAGMA user_version=0"); }
        public void Execute(string sql) { using var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path,Pooling=false }.ToString()); connection.Open(); using var command=connection.CreateCommand(); command.CommandText=sql; command.ExecuteNonQuery(); }
        public object? Scalar(string sql) { using var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path,Pooling=false }.ToString()); connection.Open(); using var command=connection.CreateCommand(); command.CommandText=sql; return command.ExecuteScalar(); }
        public string Hash()=>HashFile(Path);
        public static string HashFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        public void Dispose()=>Directory.Delete(_directory,true);
    }
}
