using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class PostgresQueryTests
{
    [Theory]
    [InlineData(1700, "public", "numeric", "b")]
    [InlineData(1700, "pg_catalog", "numeric", "d")]
    [InlineData(90000, "pg_catalog", "numeric", "b")]
    [InlineData(25, "pg_catalog", "varchar", "b")]
    public void BuiltinLookingNamesDomainsAndWrongOidsAreUnsupported(int oid, string schema, string name, string kind)
    {
        var column = PostgresQuerySql.Confirm("x", (uint)oid, schema, name, kind).Describe();
        Assert.False(column.CanSort);
        Assert.Equal(DatabaseFilterValueKind.Unsupported, column.ValueKind);
        Assert.Equal(new[] { DatabaseFilterOperator.IsNull, DatabaseFilterOperator.IsNotNull }, column.Operators);
    }

    [Fact]
    public void TypedValuesRemainParametersAndParameterNumbersAccountForSearchAndNullTests()
    {
        var table = new DatabaseTableRef("s", "t", DatabaseObjectKind.Table);
        DatabaseColumn[] columns = [new("amount", 0, "numeric", true, null), new("quote\"", 1, "text", true, null)];
        PostgresQuerySql.Column[] metadata = [PostgresQuerySql.Confirm("amount", 1700, "pg_catalog", "numeric", "b"), PostgresQuerySql.Confirm("quote\"", 25, "pg_catalog", "text", "b")];
        const string exact = "1234567890123456789012345678901234567890.123456789012345678901234567890";
        const string injection = "%'_; DROP TABLE t; --";
        var query = new DatabaseBrowseQuery("global", Filters: [new("amount", DatabaseFilterOperator.IsNotNull), new("amount", DatabaseFilterOperator.GreaterThan, exact), new("quote\"", DatabaseFilterOperator.EndsWith, injection)]);
        var sql = PostgresBrowserSql.Page(table, columns, query, queryColumns: metadata);
        Assert.Contains("q.\"amount\" OPERATOR(pg_catalog.>) ($4::pg_catalog.text)::pg_catalog.numeric", sql);
        Assert.Contains("pg_catalog.right", sql);
        Assert.Contains("$5::pg_catalog.text", sql);
        Assert.DoesNotContain(exact, sql);
        Assert.DoesNotContain(injection, sql);
        Assert.DoesNotContain("LIKE", sql);
        Assert.Equal(new[] { exact, injection }, PostgresQuerySql.Filters(query, metadata, 4).Values);
    }

    [Theory]
    [InlineData("int2", "32768")]
    [InlineData("int4", "2147483648")]
    [InlineData("int8", "9223372036854775808")]
    [InlineData("numeric", "1,25")]
    [InlineData("numeric", "NaN")]
    [InlineData("numeric", "1;SELECT 1")]
    [InlineData("date", "2026-02-30")]
    [InlineData("date", "15.10.2026")]
    [InlineData("bool", "1")]
    public void MalformedNativeInputsFailWithoutEchoingTheirValue(string cast, string value)
    {
        var error = Assert.Throws<DatabaseBrowserException>(() => PostgresQuerySql.ValidateValue(new("x", DatabaseFilterValueKind.Number, cast), value));
        Assert.Equal("filter_value_invalid", error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(value, error.Message);
    }
}
