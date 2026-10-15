using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class NativeSqliteQueryTests
{
    static NativeSqliteQueryTests() => SQLitePCL.Batteries_V2.Init();

    [Fact]
    public async Task CapabilitiesDescribeDeclaredKindsWithoutPromisingActualStorage()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY, amount DECIMAL(20,4), note VARCHAR(50), day DATE, flag BOOLEAN, raw, payload BLOB, timestamp DATETIME)");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var capabilities = await browser.GetQueryCapabilitiesAsync(await Table(browser));
        Assert.Equal(new[] { DatabaseFilterValueKind.Integer, DatabaseFilterValueKind.Number, DatabaseFilterValueKind.Text,
            DatabaseFilterValueKind.Date, DatabaseFilterValueKind.Boolean, DatabaseFilterValueKind.Unsupported,
            DatabaseFilterValueKind.Unsupported, DatabaseFilterValueKind.Unsupported }, capabilities.Columns.Select(column => column.ValueKind));
        Assert.All(capabilities.Columns, column => Assert.True(column.CanSort));
        Assert.Equal(new[] { DatabaseFilterOperator.IsNull, DatabaseFilterOperator.IsNotNull }, capabilities.Columns[5].Operators);
        Assert.All(capabilities.Columns, column => Assert.False(string.IsNullOrWhiteSpace(column.Limitation)));
        Assert.Contains(capabilities.Diagnostics, diagnostic => diagnostic.Code == "sqlite_dynamic_types");
        Assert.Contains(capabilities.Diagnostics, diagnostic => diagnostic.Code == "sqlite_native_storage_order");
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task ExactIntegerComparisonsAndNativeSortHandleTwoTenAndInt64Boundaries()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY, amount INTEGER); INSERT INTO data VALUES(1,10),(2,2),(3,9223372036854775807),(4,-9223372036854775808),(5,NULL)");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = await Table(browser);
        var sorted = await browser.ReadPageAsync(table, 0, 10, new DatabaseBrowseQuery(SortColumn: "amount"));
        Assert.Equal(new string?[] { null, "-9223372036854775808", "2", "10", "9223372036854775807" }, sorted.Rows.Select(row => row.Values[1]));
        var less = await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("amount", DatabaseFilterOperator.LessThan, "10")));
        Assert.Equal(new[] { "2", "4" }, less.Rows.Select(row => row.Values[0]));
        foreach (var value in new[] { "9223372036854775807", "-9223372036854775808" })
            Assert.Equal(value, Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("amount", DatabaseFilterOperator.Equal, value)))).Rows).Values[1]);
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task DateBooleanNullEmptyAndWhitespaceRemainDistinct()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY, day DATE, flag BOOLEAN, note TEXT); INSERT INTO data VALUES(1,'2024-02-29',1,'alpha'),(2,'2025-01-01',0,''),(3,NULL,NULL,' '),(4,'2023-12-31',1,NULL)");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = await Table(browser);
        var combined = await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("day", DatabaseFilterOperator.GreaterOrEqual, "2024-01-01"), new DatabaseFilter("flag", DatabaseFilterOperator.Equal, "TRUE")));
        Assert.Equal("1", Assert.Single(combined.Rows).Values[0]);
        Assert.Equal("2", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("flag", DatabaseFilterOperator.Equal, "false")))).Rows).Values[0]);
        Assert.Equal("3", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("day", DatabaseFilterOperator.IsNull)))).Rows).Values[0]);
        Assert.Equal("4", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.IsNull)))).Rows).Values[0]);
        Assert.Equal("2", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.Equal, "")))).Rows).Values[0]);
        Assert.Equal("3", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.Equal, " ")))).Rows).Values[0]);
        Assert.Equal(hash, file.Hash());
    }

    [Theory]
    [InlineData("INTEGER", "'incompatible'")]
    [InlineData("INTEGER", "1.5")]
    [InlineData("NUMERIC", "x'1234'")]
    [InlineData("TEXT", "x'1234'")]
    [InlineData("BOOLEAN", "2")]
    [InlineData("BOOLEAN", "'true'")]
    [InlineData("DATE", "'2023-02-29'")]
    [InlineData("DATE", "'2024-2-29'")]
    [InlineData("DATE", "'2024-02-29T00:00:00'")]
    [InlineData("DATE", "123")]
    public async Task IncompatibleStorageOutsideThePageIsRejectedBeforeSearchOrPagination(string declaration, string bad)
    {
        using var file = new Fixture();
        var valid = declaration switch { "TEXT" => "'ok'", "DATE" => "'2024-02-29'", _ => "1" };
        file.Execute($"CREATE TABLE data(id INTEGER PRIMARY KEY, value {declaration}); INSERT INTO data VALUES(1,{valid}),(1000,{bad})");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = await Table(browser);
        var value = declaration switch { "TEXT" => "ok", "DATE" => "2024-02-29", _ => "1" };
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 1,
            new DatabaseBrowseQuery(SearchText: "no rows match", Filters: [new DatabaseFilter("value", DatabaseFilterOperator.Equal, value)])));
        Assert.Equal("filter_storage_incompatible", error.Code);
        // NULL tests are valid even when non-null classes are heterogeneous.
        Assert.Equal(2, (await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("value", DatabaseFilterOperator.IsNotNull)))).Rows.Count);
        Assert.Equal(hash, file.Hash());
    }

    [Theory]
    [InlineData("INTEGER", "9223372036854775808")]
    [InlineData("INTEGER", "1.0")]
    [InlineData("NUMERIC", "9007199254740993.1")]
    [InlineData("NUMERIC", "0.10000000000000001")]
    [InlineData("NUMERIC", "1e-400")]
    [InlineData("NUMERIC", "1e400")]
    [InlineData("NUMERIC", "NaN")]
    [InlineData("NUMERIC", "Infinity")]
    [InlineData("NUMERIC", "1,5")]
    [InlineData("BOOLEAN", "yes")]
    [InlineData("BOOLEAN", "2")]
    [InlineData("DATE", "2025-02-29")]
    public async Task InvalidOrLossyInputFailsExplicitly(string declaration, string input)
    {
        using var file = new Fixture();
        file.Execute($"CREATE TABLE data(value {declaration})");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = await Table(browser);
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(
            table, 0, 10, Query(new DatabaseFilter("value", DatabaseFilterOperator.Equal, input))));
        Assert.Equal("filter_value_invalid", error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(input, error.Message);
    }

    [Theory]
    [InlineData("0.1", "1")]
    [InlineData("+0.1000e+0", "1")]
    [InlineData("1e1", "2")]
    [InlineData("9223372036854775807", "3")]
    [InlineData("-9223372036854775808", "4")]
    public async Task NumericInputUsesExactInt64OrRoundTripFiniteDouble(string input, string expected)
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY, amount NUMERIC); INSERT INTO data VALUES(1,0.1),(2,10),(3,9223372036854775807),(4,-9223372036854775808)");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var result = await browser.ReadPageAsync(await Table(browser), 0, 10, Query(new DatabaseFilter("amount", DatabaseFilterOperator.Equal, input)));
        Assert.Equal(expected, Assert.Single(result.Rows).Values[0]);
    }

    [Fact]
    public async Task TextOperationsAreLiteralCaseSensitiveAndHandleEmbeddedNullSuffixes()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT COLLATE NOCASE); INSERT INTO data VALUES(1,'prefix_%suffix'),(2,'PREFIX_%SUFFIX'),(3,''' OR 1=1;--'),(4,CAST(x'610062' AS TEXT)),(5,''),(6,NULL)");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = await Table(browser);
        Assert.Equal("1", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.Contains, "_%suffix")))).Rows).Values[0]);
        Assert.Equal("1", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.StartsWith, "prefix_")))).Rows).Values[0]);
        Assert.Equal("1", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.EndsWith, "%suffix")))).Rows).Values[0]);
        Assert.Equal("3", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.Equal, "' OR 1=1;--")))).Rows).Values[0]);
        Assert.Equal("4", Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.EndsWith, "b")))).Rows).Values[0]);
        Assert.Equal(5, (await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("note", DatabaseFilterOperator.EndsWith, "")))).Rows.Count);
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task FiltersSearchAndNumericSortApplyBeforePagination()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i+1 FROM n WHERE i<150) INSERT INTO data SELECT i,CASE WHEN i%2=0 THEN 'match_%' ELSE 'other' END FROM n");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var query = new DatabaseBrowseQuery("match_%", "id", Filters:
            [new DatabaseFilter("id", DatabaseFilterOperator.GreaterThan, "50"), new DatabaseFilter("id", DatabaseFilterOperator.LessThan, "100")]);
        var page = await browser.ReadPageAsync(await Table(browser), 20, 3, query);
        Assert.Equal(new[] { "92", "94", "96" }, page.Rows.Select(row => row.Values[0]));
        Assert.True(page.HasMore);
        var last = await browser.ReadPageAsync(await Table(browser), 23, 3, query);
        Assert.Equal("98", Assert.Single(last.Rows).Values[0]);
        Assert.False(last.HasMore);
        var descending = await browser.ReadPageAsync(await Table(browser), 0, 3, query with { Descending = true });
        Assert.Equal(new[] { "98", "96", "94" }, descending.Rows.Select(row => row.Values[0]));
    }

    [Fact]
    public async Task UnsupportedTypesPermitNullTestsAndNativeSortingButRejectComparisons()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(value); INSERT INTO data VALUES(NULL),(2),(10),('1'),(x'ff')");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = await Table(browser);
        var sorted = await browser.ReadPageAsync(table, 0, 10, new DatabaseBrowseQuery(SortColumn: "value"));
        Assert.Equal(new string?[] { null, "2", "10", "1", "0xFF" }, sorted.Rows.Select(row => row.Values[0]));
        Assert.Single((await browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("value", DatabaseFilterOperator.IsNull)))).Rows);
        var unsupported = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("value", DatabaseFilterOperator.Equal, "2"))));
        Assert.Equal("filter_operation_unsupported", unsupported.Code);
        var unknown = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 10, Query(new DatabaseFilter("value\"; DELETE FROM data;--", DatabaseFilterOperator.IsNull))));
        Assert.Equal("filter_column_unavailable", unknown.Code);
    }

    [Fact]
    public async Task PreviouslyLoadedCapabilitiesDoNotBypassFreshFullStorageValidation()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(value INTEGER); INSERT INTO data VALUES(1)");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = await Table(browser);
        Assert.Equal(DatabaseFilterValueKind.Integer, Assert.Single((await browser.GetQueryCapabilitiesAsync(table)).Columns).ValueKind);
        file.Execute("INSERT INTO data VALUES('changed storage')");
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 1, Query(new DatabaseFilter("value", DatabaseFilterOperator.Equal, "1"))));
        Assert.Equal("filter_storage_incompatible", error.Code);
    }

    [Fact]
    public async Task Utf16TextSuffixAndDateFiltersPreserveNativeEncoding()
    {
        using var file = new Fixture();
        file.Execute("PRAGMA encoding='UTF-16le'; CREATE TABLE data(note TEXT,day DATE); INSERT INTO data VALUES('Привет 🌍','2024-02-29'),('Другое','2023-01-01')");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var result = await browser.ReadPageAsync(await Table(browser), 0, 10, Query(
            new DatabaseFilter("note", DatabaseFilterOperator.EndsWith, "🌍"), new DatabaseFilter("day", DatabaseFilterOperator.GreaterThan, "2024-01-01")));
        Assert.Equal("Привет 🌍", Assert.Single(result.Rows).Values[0]);
        Assert.Equal(hash, file.Hash());
    }

    private static DatabaseBrowseQuery Query(params DatabaseFilter[] filters) => new(Filters: filters);
    private static async Task<DatabaseTableRef> Table(IDatabaseBrowser browser) => Assert.Single(await browser.GetTablesAsync());

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(TestPaths.Root, "native-sqlite-query", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "ordinary.db");
            Execute("PRAGMA user_version=0");
        }
        public void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        public string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path)));
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
