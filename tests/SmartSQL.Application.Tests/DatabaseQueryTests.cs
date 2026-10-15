using SmartSQL.Domain;

namespace SmartSQL.Application.Tests;

public sealed class DatabaseQueryTests
{
    [Xunit.Fact]
    public void NullTestsAndEmptyTextRemainDistinct()
    {
        new DatabaseFilter("name", DatabaseFilterOperator.Equal, "").Validate();
        new DatabaseFilter("name", DatabaseFilterOperator.IsNull).Validate();
        new DatabaseFilter("name", DatabaseFilterOperator.IsNotNull).Validate();
        Xunit.Assert.Throws<ArgumentException>(() => new DatabaseFilter("name", DatabaseFilterOperator.Equal).Validate());
        Xunit.Assert.Throws<ArgumentException>(() => new DatabaseFilter("name", DatabaseFilterOperator.IsNull, "").Validate());
    }

    [Xunit.Fact]
    public void FilterLimitsAndUnicodeAreValidatedBeforeProviderWork()
    {
        var valid = new DatabaseFilter("odd\".column 🌍", DatabaseFilterOperator.Equal, "%_\\'\" 🌍");
        valid.Validate();
        new DatabaseFilter("x", DatabaseFilterOperator.Equal, new string('a', DatabaseFilter.MaxValueCharacters)).Validate();
        Xunit.Assert.Throws<ArgumentException>(() => new DatabaseFilter("", DatabaseFilterOperator.Equal, "x").Validate());
        Xunit.Assert.Throws<ArgumentException>(() => new DatabaseFilter("x", (DatabaseFilterOperator)99, "x").Validate());
        foreach (var invalid in new[] { "a\0b", "\ud800", "\udc00", new string('a', DatabaseFilter.MaxValueCharacters + 1) })
            Xunit.Assert.Throws<ArgumentException>(() => new DatabaseFilter("x", DatabaseFilterOperator.Equal, invalid).Validate());
        var filters = Enumerable.Repeat(valid, DatabaseBrowseQuery.MaxFilters).ToArray();
        new DatabaseBrowseQuery(Filters: filters).Validate();
        Xunit.Assert.Throws<ArgumentException>(() => new DatabaseBrowseQuery(Filters: filters.Append(valid).ToArray()).Validate());
        Xunit.Assert.Throws<ArgumentException>(() => new DatabaseBrowseQuery(Filters: [null!]).Validate());
    }

    [Xunit.Fact]
    public void QuerySnapshotPreservesSubmittedConditionsWhileCallerChangesItsList()
    {
        var original = new List<DatabaseFilter> { new("amount", DatabaseFilterOperator.GreaterThan, "10") };
        var query = new DatabaseBrowseQuery("literal", "amount", true, original).Snapshot();
        original.Clear();
        Xunit.Assert.Equal("10", Xunit.Assert.Single(query.EffectiveFilters).Value);
        Xunit.Assert.Equal("literal", query.SearchText);
        Xunit.Assert.Equal("amount", query.SortColumn);
        Xunit.Assert.True(query.Descending);
    }

    [Xunit.Fact]
    public async Task LegacyBrowserCannotSilentlyIgnoreTypedConditions()
    {
        IDatabaseBrowser browser = new LegacyBrowser();
        var table = new DatabaseTableRef("main", "items", DatabaseObjectKind.Table);
        var capabilities = await browser.GetQueryCapabilitiesAsync(table);
        Xunit.Assert.Empty(capabilities.Columns);
        var error = await Xunit.Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.ReadPageAsync(table, 0, 10,
            new DatabaseBrowseQuery(Filters: [new("amount", DatabaseFilterOperator.GreaterThan, "10")])));
        Xunit.Assert.Equal("browse_query_unsupported", error.Code);
    }

    private sealed class LegacyBrowser : IDatabaseBrowser
    {
        public Task<IReadOnlyList<DatabaseTableRef>> GetTablesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(DatabaseTableRef table, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A filtered request must never reach the legacy reader.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
