using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

internal static class PostgresDeletionSql
{
    internal static readonly string TableSafety = PostgresEditingSql.TableSafety
        .Replace("r.ev_type='2'", "r.ev_type='4'", StringComparison.Ordinal)
        .Replace("AND pg_catalog.has_table_privilege(c.oid,'SELECT') AND pg_catalog.has_table_privilege(c.oid,'UPDATE')",
            "AND pg_catalog.has_table_privilege(c.oid,'SELECT') AND pg_catalog.has_table_privilege(c.oid,'DELETE')", StringComparison.Ordinal);

    // Even non-mutating incoming actions need child metadata/DDL guards to prove
    // their RI queries safe. This first slice deliberately rejects every incoming FK.
    internal const string IncomingForeignKeys = """
        SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint k WHERE k.contype='f' AND k.confrelid=$1),
               COALESCE((SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(k.oid,k.conrelid,k.confdeltype,k.convalidated,k.condeferrable,k.condeferred,pg_catalog.pg_get_constraintdef(k.oid)) ORDER BY k.oid)
                 FROM pg_catalog.pg_constraint k WHERE k.contype='f' AND k.confrelid=$1),'[]'::pg_catalog.jsonb)::pg_catalog.text
        """;

    // SHARE ROW EXCLUSIVE already excludes other target writers. SELECT FOR UPDATE
    // would additionally require UPDATE privilege and break a DELETE-only session.
    internal static string ReadOriginal(DatabaseTableRef table, IReadOnlyList<PostgresEditColumn> columns, IReadOnlyList<PostgresEditColumn> keys) =>
        $"SELECT {PostgresEditingSql.Projection(columns)}, q.xmin::pg_catalog.text FROM ONLY {PostgresEditingSql.Qualified(table)} AS q WHERE {PostgresEditingSql.KeyPredicate(keys)}";

    internal static string Delete(DatabaseTableRef table, IReadOnlyList<PostgresEditColumn> columns, IReadOnlyList<PostgresEditColumn> keys) =>
        $"DELETE FROM ONLY {PostgresEditingSql.Qualified(table)} AS q WHERE {PostgresEditingSql.KeyPredicate(keys)} "
            + $"RETURNING {PostgresEditingSql.Projection(columns)}, q.xmin::pg_catalog.text";
}
