using SmartSQL.Domain;

namespace SmartSQL.Application.Tests;

public sealed class DatabaseBrowserContractTests
{
    [Xunit.Fact]
    public void BrowseQueryAcceptsLiteralUnicodeAndRejectsUnrepresentableOrUnboundedSearch()
    {
        new DatabaseBrowseQuery("%_\\'\" 🌍", "Ключ 🌍", true).Validate();
        new DatabaseBrowseQuery(new string('x', DatabaseBrowseQuery.MaxSearchCharacters)).Validate();
        string[] invalid = [new string('x', DatabaseBrowseQuery.MaxSearchCharacters + 1), "a\0b", new string((char)0xD800, 1), new string((char)0xDC00, 1)];
        foreach (var value in invalid)
            Xunit.Assert.ThrowsAny<ArgumentException>(() => new DatabaseBrowseQuery(value).Validate());
        Xunit.Assert.ThrowsAny<ArgumentException>(() => new DatabaseBrowseQuery(SortColumn: string.Empty).Validate());
        Xunit.Assert.ThrowsAny<ArgumentException>(() => new DatabaseBrowseQuery(SortColumn: "a\0b").Validate());
    }

    [Xunit.Fact]
    public void NativeCatalogAndDisplayValuesDoNotAssumeManagedFieldTypesOrRowIds()
    {
        var table = new DatabaseTableRef("customer data", "orders.with.dots", DatabaseObjectKind.View);
        DatabaseColumn[] columns = [new("amount", 0, "numeric(1000,400)", false, 2), new("document", 1, "jsonb", true, null)];
        var row = new DatabaseDisplayRow(["1234567890123456789012345678901234567890.0001", null]);
        var page = new DatabasePage(table, columns, [row], 500, 100, true,
            [new DatabaseDiagnostic("unordered_page", "Порядок строк не гарантирован без ключа.")]);

        Xunit.Assert.Equal("numeric(1000,400)", page.Columns[0].NativeType);
        Xunit.Assert.Equal(2, page.Columns[0].PrimaryKeyOrder);
        Xunit.Assert.Null(page.Rows[0].Values[1]);
        Xunit.Assert.Equal(DatabaseObjectKind.View, page.Table.Kind);
        Xunit.Assert.True(page.HasMore);
        Xunit.Assert.Equal("unordered_page", Xunit.Assert.Single(page.Diagnostics).Code);
        Xunit.Assert.DoesNotContain(typeof(DatabaseDisplayRow).GetProperties(), property => property.Name == "Id");
    }

    [Xunit.Theory]
    [Xunit.InlineData(-1L, 100)]
    [Xunit.InlineData(0L, 0)]
    [Xunit.InlineData(0L, 501)]
    public void InvalidPageRequestsAreRejectedBeforeProviderWork(long offset, int pageSize) =>
        Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseBrowseLimits.ValidatePageRequest(offset, pageSize));

    [Xunit.Fact]
    public void BrowserInterfaceRequiresAsyncCancellationAndDisposal()
    {
        Xunit.Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IDatabaseBrowser)));
        Xunit.Assert.All(typeof(IDatabaseBrowser).GetMethods().Where(method => method.DeclaringType == typeof(IDatabaseBrowser)), method =>
        {
            Xunit.Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType);
            Xunit.Assert.True(typeof(Task).IsAssignableFrom(method.ReturnType));
        });
        var error = new DatabaseBrowserException("authentication_failed", "Проверьте имя пользователя и пароль.");
        Xunit.Assert.Equal("authentication_failed", error.Code);
        Xunit.Assert.Null(error.InnerException);
    }
}
