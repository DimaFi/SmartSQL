using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class PostgresEditingTests
{
    [Fact]
    public void UpdateQuotesCompositeKeysAndNeverEmbedsValuesOrTypmods()
    {
        var table = new DatabaseTableRef("schema\"", "table\";--", DatabaseObjectKind.Table);
        PostgresEditColumn[] columns = [Column("key one", 0, 23, "pg_catalog.int4", 1),
            Column("key\"two", 1, 25, "pg_catalog.text", 2), Column("amount\"", 2, 1700, "pg_catalog.numeric")];
        var keys = columns.Take(2).ToArray();
        var sql = PostgresEditingSql.Update(table, columns, keys, columns[2]);
        Assert.Contains("UPDATE ONLY \"schema\"\"\".\"table\"\";--\"", sql);
        Assert.Contains("SET \"amount\"\"\"=$3::pg_catalog.numeric", sql);
        Assert.Contains("q.\"key one\" IS NOT DISTINCT FROM $1::pg_catalog.int4 AND q.\"key\"\"two\" IS NOT DISTINCT FROM $2::pg_catalog.text", sql);
        Assert.Contains("RETURNING", sql);
        Assert.Contains("IS NOT DISTINCT FROM $3::pg_catalog.numeric", sql);
        Assert.DoesNotContain("numeric(60,20)", sql);
        var locked = PostgresEditingSql.LockRow(table, columns, keys);
        Assert.Contains("FROM ONLY", locked);
        Assert.Contains("q.xmin::pg_catalog.text", locked);
        Assert.EndsWith("FOR UPDATE", locked);
    }

    [Theory]
    [InlineData(25u, DatabaseEditValueKind.Text, "pg_catalog.text")]
    [InlineData(1043u, DatabaseEditValueKind.Text, "pg_catalog.varchar")]
    [InlineData(21u, DatabaseEditValueKind.Integer, "pg_catalog.int2")]
    [InlineData(23u, DatabaseEditValueKind.Integer, "pg_catalog.int4")]
    [InlineData(20u, DatabaseEditValueKind.Integer, "pg_catalog.int8")]
    [InlineData(1700u, DatabaseEditValueKind.Decimal, "pg_catalog.numeric")]
    [InlineData(1082u, DatabaseEditValueKind.Date, "pg_catalog.date")]
    [InlineData(16u, DatabaseEditValueKind.Boolean, "pg_catalog.bool")]
    public void SupportedBuiltinsUseUnboundedCatalogCasts(uint oid, DatabaseEditValueKind kind, string cast)
    {
        Assert.Equal((kind, cast), PostgresEditingSql.ValueType(oid));
        Assert.Equal(cast, PostgresEditingSql.KeyType(oid));
    }

    [Theory]
    [InlineData(700u)] // real
    [InlineData(701u)] // double precision
    [InlineData(114u)] // json
    [InlineData(1184u)] // timestamptz
    [InlineData(1007u)] // integer array
    [InlineData(999999u)] // user-defined type/domain
    public void UnsupportedTypesCannotBecomeAssignmentOrIdentityCasts(uint oid)
    {
        Assert.Null(PostgresEditingSql.ValueType(oid).SqlType);
        Assert.Null(PostgresEditingSql.KeyType(oid));
    }

    [Fact]
    public void UuidIsSupportedOnlyAsIdentityAndHandleExpiresAtExactBoundary()
    {
        Assert.Null(PostgresEditingSql.ValueType(2950).SqlType);
        Assert.Equal("pg_catalog.uuid", PostgresEditingSql.KeyType(2950));
        var created = DateTimeOffset.Parse("2026-10-14T12:00:00Z");
        Assert.False(PostgresDatabaseBrowser.IsEditHandleExpired(created, created + PostgresDatabaseBrowser.EditHandleLifetime - TimeSpan.FromTicks(1)));
        Assert.True(PostgresDatabaseBrowser.IsEditHandleExpired(created, created + PostgresDatabaseBrowser.EditHandleLifetime));
        Assert.True(PostgresDatabaseBrowser.IsEditHandleExpired(created, created + TimeSpan.FromHours(1)));
    }

    private static PostgresEditColumn Column(string name, int ordinal, uint oid, string cast, int? key = null) =>
        new(new EditableDatabaseColumn(name, ordinal, "numeric(60,20)", DatabaseEditValueKind.Text, key is null, true), oid, cast, key);
}
