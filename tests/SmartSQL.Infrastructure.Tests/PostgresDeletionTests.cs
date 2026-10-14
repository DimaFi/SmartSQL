using System.Text.Json;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class PostgresDeletionTests
{
    [Fact]
    public void DeleteAndOriginalReadUseOnlyQuotedCompositeKeysAndNativeReturningWithoutUpdatePrivilege()
    {
        var table = new DatabaseTableRef("schema\"", "table\";--", DatabaseObjectKind.Table);
        PostgresEditColumn[] columns = [Column("tenant\"", 0, 25, "pg_catalog.text", 1),
            Column("amount", 1, 1700, "pg_catalog.numeric", 2), Column("note", 2, 25, "pg_catalog.text")];
        var keys = columns.Take(2).ToArray();
        var sql = PostgresDeletionSql.Delete(table, columns, keys);
        Assert.StartsWith("DELETE FROM ONLY \"schema\"\"\".\"table\"\";--\" AS q WHERE", sql);
        Assert.Contains("q.\"tenant\"\"\" IS NOT DISTINCT FROM $1::pg_catalog.text", sql);
        Assert.Contains("q.\"amount\" IS NOT DISTINCT FROM $2::pg_catalog.numeric", sql);
        Assert.Contains("RETURNING q.\"tenant\"\"\"::pg_catalog.text", sql);
        Assert.EndsWith("q.xmin::pg_catalog.text", sql);
        Assert.DoesNotContain("numeric(", sql);
        var original = PostgresDeletionSql.ReadOriginal(table, columns, keys);
        Assert.Contains("FROM ONLY", original);
        Assert.Contains("q.xmin::pg_catalog.text", original);
        Assert.DoesNotContain("FOR UPDATE", original);
        Assert.DoesNotContain("FOR KEY SHARE", original);
    }

    [Fact]
    public void SafetyRequiresIndependentDeleteRightsAndFingerprintIncludesEveryIncomingForeignKey()
    {
        Assert.Contains("has_table_privilege(c.oid,'SELECT') AND pg_catalog.has_table_privilege(c.oid,'DELETE')", PostgresDeletionSql.TableSafety);
        Assert.DoesNotContain("has_table_privilege(c.oid,'UPDATE')", PostgresDeletionSql.TableSafety);
        Assert.Contains("has_table_privilege(c.oid,'UPDATE,DELETE,TRUNCATE')", PostgresDeletionSql.TableSafety);
        Assert.Contains("r.ev_type='4'", PostgresDeletionSql.TableSafety);
        Assert.Contains("k.contype='f' AND k.confrelid=$1", PostgresDeletionSql.IncomingForeignKeys);
        Assert.Contains("k.confdeltype", PostgresDeletionSql.IncomingForeignKeys);
        Assert.Contains("pg_catalog.pg_get_constraintdef(k.oid)", PostgresDeletionSql.IncomingForeignKeys);
        Assert.DoesNotContain("confdeltype IN", PostgresDeletionSql.IncomingForeignKeys);
    }

    [Fact]
    public void DeleteHandleIsIndependentTransientAndExcludedFromDisplayAndSerializedRows()
    {
        var edit = Guid.NewGuid();
        var delete = Guid.NewGuid();
        var row = new DatabaseDisplayRow(["original", null]) { EditHandle = edit, DeleteHandle = delete };
        Assert.NotEqual(row.EditHandle, row.DeleteHandle);
        var json = JsonSerializer.Serialize(row);
        Assert.DoesNotContain("EditHandle", json);
        Assert.DoesNotContain("DeleteHandle", json);
        Assert.DoesNotContain(edit.ToString(), json);
        Assert.DoesNotContain(delete.ToString(), json);
        Assert.DoesNotContain(delete.ToString(), row.ToString());
        Assert.DoesNotContain("DeleteHandle", row.ToString());
        var request = new DatabaseRowDelete(new("public", "rows", DatabaseObjectKind.Table), delete);
        Assert.DoesNotContain(delete.ToString(), request.ToString());
    }

    private static PostgresEditColumn Column(string name, int ordinal, uint oid, string cast, int? key = null) =>
        new(new EditableDatabaseColumn(name, ordinal, "native", DatabaseEditValueKind.Text, false, true), oid, cast, key);
}
