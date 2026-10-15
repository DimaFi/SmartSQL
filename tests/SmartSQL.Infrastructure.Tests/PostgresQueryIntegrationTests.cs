using Npgsql;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>Unique synthetic tables and domains in the pre-existing dedicated loopback fixture only.</summary>
public sealed class PostgresQueryIntegrationTests
{
    private const string Exact = "1234567890123456789012345678901234567890.123456789012345678901234567890";
    private const string Literal = "%'_\"; DROP TABLE browser_rows; --";

    [DedicatedPostgresFact]
    public async Task NativeNumericSortExactNumericDateBooleanNullAndEmptyStayDistinct()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var ascending = await browser.ReadPageAsync(fixture.Table, 0, 100, new DatabaseBrowseQuery(SortColumn: "amount"));
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, Ids(ascending));
        Assert.Equal(new[] { "5", "4", "3", "2", "1" }, Ids(await browser.ReadPageAsync(fixture.Table, 0, 100, new DatabaseBrowseQuery(SortColumn: "amount", Descending: true))));
        Assert.Equal(new[] { "2" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("amount", DatabaseFilterOperator.Equal, "2"))));
        Assert.Equal(new[] { "5" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("amount", DatabaseFilterOperator.Equal, Exact))));
        Assert.Empty((await Filter(browser, fixture.Table, new DatabaseFilter("amount", DatabaseFilterOperator.Equal, Exact + "1"))).Rows);
        Assert.Equal(new[] { "2", "3" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("stamp", DatabaseFilterOperator.GreaterOrEqual, "2026-10-15"), new DatabaseFilter("stamp", DatabaseFilterOperator.LessThan, "2026-10-17"))));
        Assert.Equal(new[] { "1", "3", "5" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("enabled", DatabaseFilterOperator.Equal, "true"))));
        Assert.Equal(new[] { "1" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.IsNull))));
        Assert.Equal(new[] { "2" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.Equal, ""))));
    }

    [DedicatedPostgresFact]
    public async Task AndAndGlobalLiteralSearchApplyBeforePagingAndInjectionShapedTextIsLiteral()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var query = new DatabaseBrowseQuery("2026-10", "id", Filters: [new DatabaseFilter("id", DatabaseFilterOperator.GreaterOrEqual, "2"), new DatabaseFilter("id", DatabaseFilterOperator.LessOrEqual, "4")]);
        var first = await browser.ReadPageAsync(fixture.Table, 0, 2, query);
        var last = await browser.ReadPageAsync(fixture.Table, 2, 2, query);
        Assert.Equal(new[] { "2", "3" }, Ids(first));
        Assert.True(first.HasMore);
        Assert.Equal(new[] { "4" }, Ids(last));
        Assert.False(last.HasMore);
        foreach (var operation in new[] { DatabaseFilterOperator.Equal, DatabaseFilterOperator.Contains, DatabaseFilterOperator.StartsWith, DatabaseFilterOperator.EndsWith })
            Assert.Equal(new[] { "4" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("note", operation, Literal))));
        Assert.Equal(new[] { "3", "5" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.StartsWith, "Alpha"))));
        Assert.Equal(new[] { "3" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.EndsWith, "tail"))));
        Assert.Empty((await Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.Equal, Literal + " OR true"))).Rows);
        Assert.Equal(5, (await browser.ReadPageAsync(fixture.Table, 0, 100)).Rows.Count);
    }

    [DedicatedPostgresFact]
    public async Task CapabilitiesUseBuiltinIdentityRejectDomainsAndAreRecheckedAfterDdl()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var capabilities = await browser.GetQueryCapabilitiesAsync(fixture.Table);
        Assert.Equal(DatabaseFilterValueKind.Number, capabilities.Columns.Single(column => column.Name == "amount").ValueKind);
        foreach (var name in new[] { "document", "domain_value" })
        {
            var column = capabilities.Columns.Single(column => column.Name == name);
            Assert.Equal(DatabaseFilterValueKind.Unsupported, column.ValueKind);
            Assert.False(column.CanSort);
            Assert.NotNull(column.Limitation);
            Assert.Equal("sort_type_unsupported", (await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(fixture.Table, 0, 100, new DatabaseBrowseQuery(SortColumn: name)))).Code);
            Assert.Equal("filter_operator_unsupported", (await Assert.ThrowsAsync<DatabaseBrowserException>(() => Filter(browser, fixture.Table, new DatabaseFilter(name, DatabaseFilterOperator.Equal, "1")))).Code);
            Assert.Equal(5, (await Filter(browser, fixture.Table, new DatabaseFilter(name, DatabaseFilterOperator.IsNull))).Rows.Count);
        }
        await fixture.ExecuteAsync($"ALTER TABLE {fixture.Qualified} ALTER COLUMN note TYPE json USING pg_catalog.to_json(note)");
        Assert.Equal("filter_operator_unsupported", (await Assert.ThrowsAsync<DatabaseBrowserException>(() => Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.Equal, "Alpha tail")))).Code);
        Assert.Equal("filter_column_unavailable", (await Assert.ThrowsAsync<DatabaseBrowserException>(() => Filter(browser, fixture.Table, new DatabaseFilter("missing\"; DROP TABLE x;--", DatabaseFilterOperator.IsNull)))).Code);
    }

    [DedicatedPostgresFact]
    public async Task InvalidInputsAreSanitizedAndTypedReadRetainsGuardedEditHandles()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var browser = await PostgresDatabaseBrowser.OpenForEditingAsync(Profile(), string.Empty);
        foreach (var filter in new DatabaseFilter[] {
            new DatabaseFilter("id", DatabaseFilterOperator.Equal, "2147483648"), new DatabaseFilter("amount", DatabaseFilterOperator.Equal, "1e9999999"),
            new DatabaseFilter("amount", DatabaseFilterOperator.Equal, "1,2"), new DatabaseFilter("stamp", DatabaseFilterOperator.Equal, "2026-02-30"),
            new DatabaseFilter("enabled", DatabaseFilterOperator.Equal, "yes") })
        {
            var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => Filter(browser, fixture.Table, filter));
            Assert.Equal("filter_value_invalid", error.Code);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain(filter.Value!, error.Message);
        }
        var malformed = await Assert.ThrowsAsync<DatabaseBrowserException>(() => Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.Equal, "bad\0value")));
        Assert.Equal("query_invalid", malformed.Code);
        var page = await Filter(browser, fixture.Table, new DatabaseFilter("id", DatabaseFilterOperator.Equal, "2"));
        var row = Assert.Single(page.Rows);
        Assert.NotNull(row.EditHandle);
        await browser.UpdateCellAsync(new DatabaseCellUpdate(fixture.Table, row.EditHandle!.Value, "note", "saved"));
        Assert.Equal(new[] { "2" }, Ids(await Filter(browser, fixture.Table, new DatabaseFilter("note", DatabaseFilterOperator.Equal, "saved"))));
    }

    private static Task<DatabasePage> Filter(PostgresDatabaseBrowser browser, DatabaseTableRef table, params DatabaseFilter[] filters) =>
        browser.ReadPageAsync(table, 0, 100, new DatabaseBrowseQuery(Filters: filters));
    private static string?[] Ids(DatabasePage page) => page.Rows.Select(row => row.Values[0]).ToArray();
    private static ConnectionProfile Profile()
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432")
            throw new InvalidOperationException("Only the dedicated loopback fixture on 15432 is authorized.");
        return new(Guid.NewGuid(), "Typed query fixture", DatabaseProviders.PostgreSql, "127.0.0.1", 15432,
            "smartsql_browser_test", "smartsql_browser_editor", ConnectionSslPolicy.Disable);
    }

    private sealed class Fixture(NpgsqlConnection connection, DatabaseTableRef table, string domain) : IAsyncDisposable
    {
        public DatabaseTableRef Table => table;
        public string Qualified => PostgresEditingSql.Qualified(table);
        public static async Task<Fixture> CreateAsync()
        {
            var settings = PostgresDatabaseBrowser.ConnectionSettings(Profile());
            settings.Options = "-c statement_timeout=15000 -c lock_timeout=3000";
            var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            var table = new DatabaseTableRef("smartsql_edit_test", "query_" + Guid.NewGuid().ToString("N"), DatabaseObjectKind.Table);
            var domain = $"\"smartsql_edit_test\".\"query_domain_{Guid.NewGuid():N}\"";
            var fixture = new Fixture(connection, table, domain);
            try
            {
                await fixture.ExecuteAsync($"CREATE DOMAIN {domain} AS numeric");
                await fixture.ExecuteAsync($"CREATE TABLE {fixture.Qualified} (id integer PRIMARY KEY, amount numeric, stamp date, enabled boolean, note text, document json, domain_value {domain})");
                await using var command = new NpgsqlCommand($"INSERT INTO {fixture.Qualified} (id,amount,stamp,enabled,note) VALUES (1,1,'2026-10-14',true,NULL),(2,2,'2026-10-15',false,''),(3,10,'2026-10-16',true,'Alpha tail'),(4,20,'2026-10-17',false,$1),(5,{Exact},'2026-10-18',true,'Alpha end')", connection);
                command.Parameters.AddWithValue(Literal);
                await command.ExecuteNonQueryAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync($"DROP TABLE IF EXISTS {Qualified}");
                await ExecuteAsync($"DROP DOMAIN IF EXISTS {domain}");
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
