using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class PostgresInsertionTests
{
    [Fact]
    public void InsertQuotesCatalogNamesAndDistinguishesOmissionFromNullAndEmpty()
    {
        var table = new DatabaseTableRef("schema\"", "table\";--", DatabaseObjectKind.Table);
        PostgresInsertColumn[] columns = [Column("key\"", 0, "pg_catalog.int4"), Column("default", 1, "pg_catalog.text"),
            Column("empty", 2, "pg_catalog.text"), Column("null", 3, "pg_catalog.text")];
        DatabaseInsertValue[] values = [new("key\"", DatabaseInsertValueMode.Value, "1"),
            new("default", DatabaseInsertValueMode.ServerDefault), new("empty", DatabaseInsertValueMode.Value, ""),
            new("null", DatabaseInsertValueMode.Null)];
        var sql = PostgresInsertionSql.Insert(table, columns, values);
        Assert.Contains("INSERT INTO \"schema\"\"\".\"table\"\";--\" AS q", sql);
        Assert.Contains("(\"key\"\"\", \"empty\", \"null\") VALUES ($1::pg_catalog.int4, $2::pg_catalog.text, $3::pg_catalog.text)", sql);
        Assert.Contains("q.\"default\"::pg_catalog.text", sql);
        Assert.Contains("IS NOT DISTINCT FROM $2::pg_catalog.text", sql);
        Assert.DoesNotContain("VALUES ('", sql);
    }

    [Fact]
    public void AllServerDefaultsUseDefaultValuesAndNativeNumericNeverHasTypmodCast()
    {
        var table = new DatabaseTableRef("public", "rows", DatabaseObjectKind.Table);
        PostgresInsertColumn[] columns = [Column("amount", 0, "pg_catalog.numeric")];
        Assert.Contains("DEFAULT VALUES RETURNING q.\"amount\"::pg_catalog.text, (true)",
            PostgresInsertionSql.Insert(table, columns, [new("amount", DatabaseInsertValueMode.ServerDefault)]));
        const string value = "123456789012345678901234567890.012345678901234567890";
        var sql = PostgresInsertionSql.Insert(table, columns, [new("amount", DatabaseInsertValueMode.Value, value)]);
        Assert.Contains("VALUES ($1::pg_catalog.numeric)", sql);
        Assert.Contains("IS NOT DISTINCT FROM $1::pg_catalog.numeric", sql);
        Assert.DoesNotContain(value, sql);
        Assert.DoesNotContain("numeric(", sql);
    }

    [Fact]
    public void InsertSafetyHasIndependentPrivilegesRulesAndAllDefaultMetadata()
    {
        Assert.Contains("has_table_privilege(c.oid,'INSERT')", PostgresInsertionSql.TableSafety);
        Assert.DoesNotContain("has_table_privilege(c.oid,'UPDATE')", PostgresInsertionSql.TableSafety);
        Assert.Contains("r.ev_type='3'", PostgresInsertionSql.TableSafety);
        Assert.Contains("server_version_num", PostgresInsertionSql.TableSafety);
        Assert.Contains("has_table_privilege(c.oid,'MAINTAIN')", PostgresInsertionSql.TableSafety);
        Assert.Contains("has_table_privilege(c.oid,'UPDATE,DELETE,TRUNCATE')", PostgresInsertionSql.TableSafety);
        Assert.Contains("d.adbin::pg_catalog.text", PostgresInsertionSql.TableSafety);
        Assert.Contains("has_sequence_privilege(seq.oid,'USAGE')", PostgresInsertionSql.Defaults);
        Assert.Contains("dep.refobjsubid=a.attnum", PostgresInsertionSql.Defaults);
        Assert.Contains("regexp_matches(d.adbin", PostgresInsertionSql.Defaults);
    }

    private static PostgresInsertColumn Column(string name, int ordinal, string cast) =>
        new(new InsertableDatabaseColumn(new EditableDatabaseColumn(name, ordinal, "native", DatabaseEditValueKind.Text, true, true), true, false),
            25, cast, ordinal == 0 ? 1 : null);
}
