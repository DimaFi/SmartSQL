using System.Globalization;
using System.Text.Json;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>Only the explicitly enabled, dedicated loopback fixture prepared by the project is accessed.</summary>
public sealed class DedicatedPostgresFactAttribute : FactAttribute
{
    public DedicatedPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT")))
            Skip = "Set SMARTSQL_POSTGRES_TEST_PORT=15432 after preparing the dedicated smartsql_browser_test fixture. No other database is accessed.";
    }
}

public sealed class PostgresDatabaseBrowserIntegrationTests
{
    [DedicatedPostgresFact]
    public async Task SearchFiltersBeforePagingPreservesNativeValuesAndClearingRestoresOldApi()
    {
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var table = Assert.Single(await browser.GetTablesAsync(), item => item.Name == "browser_rows");
        // The dedicated C-locale database folds ASCII case only; preserve the fixture's Cyrillic case.
        // ASCII case folding is verified independently against the read-only probe below.
        var query = new DatabaseBrowseQuery("Тестовая строка 20");
        var first = await browser.ReadPageAsync(table, 0, 3, query);
        var second = await browser.ReadPageAsync(table, 3, 3, query);
        var last = await browser.ReadPageAsync(table, 6, 3, query);
        var all = await browser.ReadPageAsync(table, 0, 100, query);
        Assert.Equal(7, all.Rows.Count);
        Assert.True(first.HasMore);
        Assert.True(second.HasMore);
        Assert.False(last.HasMore);
        Assert.Single(last.Rows);
        var id = all.Columns.Single(column => column.Name == "id").Ordinal;
        Assert.Equal(all.Rows.Select(row => row.Values[id]), first.Rows.Concat(second.Rows).Concat(last.Rows).Select(row => row.Values[id]));
        var amount = all.Columns.Single(column => column.Name == "amount").Ordinal;
        Assert.All(all.Rows, row => Assert.Equal("1234567890123456789012345678901234567890.12345678901234567890", row.Values[amount]));
        var old = await browser.ReadPageAsync(table, 0, 100);
        var cleared = await browser.ReadPageAsync(table, 0, 100, new DatabaseBrowseQuery(string.Empty));
        Assert.Equal(old.Rows.Select(row => row.Values[id]), cleared.Rows.Select(row => row.Values[id]));
        Assert.DoesNotContain(cleared.Diagnostics, item => item.Code == "native_text_search");
        foreach (var literal in new[] { "%", "_", "'", "\"; DROP TABLE browser_rows; --" })
            Assert.Empty((await browser.ReadPageAsync(table, 0, 100, new DatabaseBrowseQuery(literal))).Rows);
        Assert.Equal(100, (await browser.ReadPageAsync(table, 0, 100, new DatabaseBrowseQuery("\\x00ff"))).Rows.Count);
        // Quotes in PostgreSQL JSON text are literal searchable characters, not SQL delimiters.
        Assert.Equal(100, (await browser.ReadPageAsync(table, 0, 100, new DatabaseBrowseQuery("\"value\""))).Rows.Count);
        var numeric = await browser.ReadPageAsync(table, 0, 100, new DatabaseBrowseQuery("7890.12345678901234567890"));
        Assert.Equal(100, numeric.Rows.Count);
        Assert.True(numeric.HasMore);
    }

    [DedicatedPostgresFact]
    public async Task ExplicitSortUsesHonestTextOrderingNativeMetadataAndReadOnlyQueries()
    {
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var tables = await browser.GetTablesAsync();
        var table = Assert.Single(tables, item => item.Name == "browser_rows");
        var ascending = await browser.ReadPageAsync(table, 0, 500, new DatabaseBrowseQuery(SortColumn: "id"));
        var descending = await browser.ReadPageAsync(table, 0, 500, new DatabaseBrowseQuery(SortColumn: "id", Descending: true));
        var id = ascending.Columns.Single(column => column.Name == "id").Ordinal;
        var values = ascending.Rows.Select(row => row.Values[id]!).ToArray();
        Assert.Equal(values.OrderBy(value => value, StringComparer.Ordinal), values);
        Assert.Equal(values.Reverse(), descending.Rows.Select(row => row.Values[id]!));
        Assert.Equal(new[] { "1", "10", "100" }, values.Take(3));
        Assert.Contains(ascending.Diagnostics, item => item.Code == "selected_text_sort");
        Assert.Equal("integer", ascending.Columns[id].NativeType);
        // Native json and array values need no native ordering operators for the selected sort.
        var json = await browser.ReadPageAsync(table, 0, 2, new DatabaseBrowseQuery(SortColumn: "document"));
        Assert.Equal(2, json.Rows.Count);
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 100,
            new DatabaseBrowseQuery(SortColumn: "id\"; DROP TABLE browser_rows; --")));
        Assert.Equal("sort_column_unavailable", failure.Code);
        var probe = Assert.Single(tables, item => item.Name == "browser_readonly_probe");
        var readOnly = await browser.ReadPageAsync(probe, 0, 1, new DatabaseBrowseQuery("ON", "read_only", true));
        Assert.Equal("on", Assert.Single(readOnly.Rows).Values[readOnly.Columns.Single(column => column.Name == "read_only").Ordinal]);
        var quoted = Assert.Single(tables, item => item.Schema == "Кавычки \" схема");
        Assert.Single((await browser.ReadPageAsync(quoted, 0, 1, new DatabaseBrowseQuery("🌍", "ключ \" ."))).Rows);
    }

    [DedicatedPostgresFact]
    public async Task CatalogAndMetadataPreserveNativeTypesNullableColumnsAndCompositeKeyOrder()
    {
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var tables = await browser.GetTablesAsync();
        var table = Assert.Single(tables, item => item.Schema == "public" && item.Name == "browser_rows");
        Assert.Equal(DatabaseObjectKind.Table, table.Kind);
        Assert.Contains(tables, item => item.Name == "browser_readonly_probe" && item.Kind == DatabaseObjectKind.View);
        Assert.DoesNotContain(tables, item => item.Name == "browser_hidden");
        Assert.DoesNotContain(tables, item => item.Schema.StartsWith("pg_", StringComparison.Ordinal) || item.Schema == "information_schema");
        var columns = await browser.GetColumnsAsync(table);
        Assert.Equal(Enumerable.Range(0, columns.Count), columns.Select(item => item.Ordinal));
        Assert.Equal(1, Assert.Single(columns, item => item.Name == "tenant_id").PrimaryKeyOrder);
        Assert.Equal(2, Assert.Single(columns, item => item.Name == "id").PrimaryKeyOrder);
        Assert.False(Assert.Single(columns, item => item.Name == "id").IsNullable);
        Assert.True(Assert.Single(columns, item => item.Name == "note").IsNullable);
        Assert.Equal("numeric(60,20)", Assert.Single(columns, item => item.Name == "amount").NativeType);
        Assert.Contains("browser_state", Assert.Single(columns, item => item.Name == "state").NativeType);
    }

    [DedicatedPostgresFact]
    public async Task PagingUsesSentinelAndPreservesLargeNumbersNullEmptyBinaryAndNativeValues()
    {
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var table = Assert.Single(await browser.GetTablesAsync(), item => item.Schema == "public" && item.Name == "browser_rows");
        var first = await browser.ReadPageAsync(table, 0, 100);
        var second = await browser.ReadPageAsync(table, 100, 100);
        var last = await browser.ReadPageAsync(table, 200, 100);
        var exhausted = await browser.ReadPageAsync(table, 205, 100);
        Assert.Equal(100, first.Rows.Count);
        Assert.True(first.HasMore);
        Assert.Equal(100, second.Rows.Count);
        Assert.True(second.HasMore);
        Assert.Equal(5, last.Rows.Count);
        Assert.False(last.HasMore);
        Assert.Empty(exhausted.Rows);
        Assert.False(exhausted.HasMore);
        var columns = first.Columns;
        int Index(string name) => columns.Single(column => column.Name == name).Ordinal;
        var rows = first.Rows.Concat(second.Rows).Concat(last.Rows).ToArray();
        var keys = rows.Select(row => (Tenant: int.Parse(row.Values[Index("tenant_id")]!, CultureInfo.InvariantCulture),
            Id: int.Parse(row.Values[Index("id")]!, CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(205, keys.Distinct().Count());
        Assert.Equal(keys.OrderBy(key => key.Tenant).ThenBy(key => key.Id), keys);
        Assert.All(rows, row => Assert.Equal("1234567890123456789012345678901234567890.12345678901234567890", row.Values[Index("amount")]));
        Assert.Null(Assert.Single(rows, row => row.Values[Index("id")] == "1").Values[Index("note")]);
        Assert.Equal(string.Empty, Assert.Single(rows, row => row.Values[Index("id")] == "2").Values[Index("note")]);
        Assert.Equal("\\x00ff", rows[0].Values[Index("payload")]);
        Assert.Equal("{1,2,3}", rows[0].Values[Index("numbers")]);
        Assert.Contains(rows[0].Values[Index("state")], new[] { "new", "done" });
        Assert.Equal("2026-10-12 06:15:00+00", rows[0].Values[Index("occurred_at")]);
        using var json = JsonDocument.Parse(rows[0].Values[Index("document")]!);
        Assert.True(json.RootElement.GetProperty("value").GetBoolean());
        Assert.Contains(first.Diagnostics, item => item.Code == "live_pagination");
        Assert.DoesNotContain(first.Diagnostics, item => item.Code == "no_primary_key");
    }

    [DedicatedPostgresFact]
    public async Task EveryBrowseOperationHasReadOnlyTransactionAndKeylessRowsHaveHonestDiagnostics()
    {
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var tables = await browser.GetTablesAsync();
        var probe = Assert.Single(tables, item => item.Name == "browser_readonly_probe");
        var result = await browser.ReadPageAsync(probe, 0, 100);
        var row = Assert.Single(result.Rows);
        Assert.Equal("on", row.Values[result.Columns.Single(column => column.Name == "read_only").Ordinal]);
        Assert.Equal("UTC", row.Values[result.Columns.Single(column => column.Name == "timezone").Ordinal]);
        var keyless = Assert.Single(tables, item => item.Name == "browser_no_key");
        var page = await browser.ReadPageAsync(keyless, 0, 1);
        Assert.Single(page.Rows);
        Assert.True(page.HasMore);
        Assert.Contains(page.Diagnostics, item => item.Code == "no_primary_key");
        Assert.Contains(page.Diagnostics, item => item.Code == "unstable_pagination");
    }

    [DedicatedPostgresFact]
    public async Task QuotedCatalogNamesAreUsedLiterallyAndMissingObjectsCannotBecomeSql()
    {
        const string secret = "transient-secret-never-in-errors";
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), secret);
        var table = Assert.Single(await browser.GetTablesAsync(), item => item.Schema == "Кавычки \" схема" && item.Name == "таблица \" ; DROP TABLE x; --");
        var page = await browser.ReadPageAsync(table, 0, 100);
        Assert.Equal(2, page.Columns.Count);
        Assert.Equal("ключ \" .", page.Columns[0].Name);
        Assert.Equal("значение", page.Columns[1].Name);
        Assert.Equal("1", Assert.Single(page.Rows).Values[0]);
        Assert.Contains("🌍", Assert.Single(page.Rows).Values[1]);
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(new DatabaseTableRef("public", secret + "\"; DROP TABLE browser_rows; --", DatabaseObjectKind.Table), 0, 100));
        Assert.Equal("object_unavailable", failure.Code);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(secret, failure.ToString());
        Assert.Contains(await browser.GetTablesAsync(), item => item.Name == "browser_rows");
    }

    [DedicatedPostgresFact]
    public async Task CancellationPageValidationAndDisposalPreventFurtherCommands()
    {
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var table = Assert.Single(await browser.GetTablesAsync(), item => item.Name == "browser_rows");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => browser.ReadPageAsync(table, 0, 100, cancelled.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => browser.ReadPageAsync(table, -1, 100));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => browser.ReadPageAsync(table, 0, 501));
        var changedKind = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetColumnsAsync(table with { Kind = DatabaseObjectKind.View }));
        Assert.Equal("object_changed", changedKind.Code);
        await browser.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => browser.GetTablesAsync());
    }

    [DedicatedPostgresFact]
    public async Task LargeDatabaseValueFailsExplicitlyWithoutTruncationOrPartialResult()
    {
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var table = Assert.Single(await browser.GetTablesAsync(), item => item.Name == "browser_large_value");
        var failure = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 100));
        Assert.Equal("cell_too_large", failure.Code);
        Assert.Null(failure.InnerException);
        Assert.Contains(await browser.GetTablesAsync(), item => item.Name == "browser_rows");
    }

    private static ConnectionProfile Profile()
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432")
            throw new InvalidOperationException("Integration tests access only the dedicated loopback fixture on port 15432.");
        return new ConnectionProfile(Guid.NewGuid(), "Dedicated integration fixture", DatabaseProviders.PostgreSql,
            "127.0.0.1", 15432, "smartsql_browser_test", "smartsql_browser_reader", ConnectionSslPolicy.Disable);
    }
}
