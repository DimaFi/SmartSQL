using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

internal static class PostgresInsertionSql
{
    // The same structural checks apply to both actions, but their privileges and
    // rewrite events are independent. INSERT must not require UPDATE permission.
    internal static readonly string TableSafety = PostgresEditingSql.TableSafety
        .Replace("r.ev_type='2'", "r.ev_type='3'", StringComparison.Ordinal)
        .Replace("AND pg_catalog.has_table_privilege(c.oid,'SELECT') AND pg_catalog.has_table_privilege(c.oid,'UPDATE')",
            "AND pg_catalog.has_table_privilege(c.oid,'SELECT') AND pg_catalog.has_table_privilege(c.oid,'INSERT')", StringComparison.Ordinal);

    internal const string Defaults = """
        SELECT a.attname::pg_catalog.text,a.attidentity<>'',a.attgenerated<>'',t.typtype='d',
               d.oid IS NOT NULL,
               CASE
                 WHEN t.typtype='d' THEN false
                 WHEN a.attgenerated<>'' THEN true
                 WHEN a.attidentity<>'' THEN s.oid IS NOT NULL AND s.allowed
                 WHEN d.oid IS NULL THEN NOT a.attnotnull AND NOT t.typnotnull
                 WHEN pg_catalog.pg_get_expr(d.adbin,d.adrelid)=pg_catalog.format('nextval(%L::regclass)',s.oid::pg_catalog.regclass::pg_catalog.text)
                   THEN s.oid IS NOT NULL AND s.allowed AND a.atttypid IN (20,21,23)
                 ELSE
                   NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dep
                     LEFT JOIN pg_catalog.pg_operator op ON dep.refclassid='pg_catalog.pg_operator'::pg_catalog.regclass AND op.oid=dep.refobjid
                     JOIN pg_catalog.pg_proc p ON p.oid=CASE WHEN dep.refclassid='pg_catalog.pg_proc'::pg_catalog.regclass THEN dep.refobjid ELSE op.oprcode END
                     WHERE dep.classid='pg_catalog.pg_attrdef'::pg_catalog.regclass AND dep.objid=d.oid
                       AND (p.pronamespace<>'pg_catalog'::pg_catalog.regnamespace OR p.provolatile='v' OR op.oprnamespace<>'pg_catalog'::pg_catalog.regnamespace))
                   AND NOT EXISTS (SELECT 1 FROM pg_catalog.regexp_matches(d.adbin::pg_catalog.text,':(funcid|opfuncid|opno) ([0-9]+)','g') m(parts)
                     LEFT JOIN pg_catalog.pg_operator op ON m.parts[1]='opno' AND op.oid=m.parts[2]::pg_catalog.oid
                     JOIN pg_catalog.pg_proc p ON p.oid=CASE WHEN m.parts[1]='opno' THEN op.oprcode::pg_catalog.oid ELSE m.parts[2]::pg_catalog.oid END
                     WHERE p.pronamespace<>'pg_catalog'::pg_catalog.regnamespace OR p.provolatile='v' OR op.oprnamespace<>'pg_catalog'::pg_catalog.regnamespace)
                   AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dep JOIN pg_catalog.pg_type typ ON typ.oid=dep.refobjid
                     WHERE dep.classid='pg_catalog.pg_attrdef'::pg_catalog.regclass AND dep.objid=d.oid
                       AND dep.refclassid='pg_catalog.pg_type'::pg_catalog.regclass AND typ.typnamespace<>'pg_catalog'::pg_catalog.regnamespace)
               END,
               s.oid IS NOT NULL,
               COALESCE(s.allowed,false),s.oid
        FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_type t ON t.oid=a.atttypid
        LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
        LEFT JOIN LATERAL (
          SELECT seq.oid,(pg_catalog.has_sequence_privilege(seq.oid,'USAGE') OR pg_catalog.has_sequence_privilege(seq.oid,'UPDATE')) AS allowed
          FROM pg_catalog.pg_depend dep JOIN pg_catalog.pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
          WHERE dep.classid='pg_catalog.pg_class'::pg_catalog.regclass AND dep.refclassid='pg_catalog.pg_class'::pg_catalog.regclass
            AND dep.refobjid=a.attrelid AND dep.refobjsubid=a.attnum AND dep.deptype IN ('a','i')
          LIMIT 1) s ON true
        WHERE a.attrelid=$1 AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum
        """;

    internal static string Insert(DatabaseTableRef table, IReadOnlyList<PostgresInsertColumn> columns,
        IReadOnlyList<DatabaseInsertValue> values)
    {
        var supplied = values.Where(v => v.Mode != DatabaseInsertValueMode.ServerDefault).ToArray();
        var byName = columns.ToDictionary(c => c.Capability.Column.Name, StringComparer.Ordinal);
        var prefix = $"INSERT INTO {PostgresEditingSql.Qualified(table)} AS q ";
        var input = supplied.Length == 0 ? "DEFAULT VALUES"
            : "(" + string.Join(", ", supplied.Select(v => PostgresBrowserSql.QuoteIdentifier(v.ColumnName))) + ") VALUES ("
                + string.Join(", ", supplied.Select((v, index) => $"${index + 1}::{byName[v.ColumnName].SqlType}")) + ")";
        var projection = string.Join(", ", columns.Select(c => $"q.{PostgresBrowserSql.QuoteIdentifier(c.Capability.Column.Name)}::pg_catalog.text"));
        var exact = supplied.Length == 0 ? "true" : string.Join(" AND ", supplied.Select((v, index) =>
            $"(q.{PostgresBrowserSql.QuoteIdentifier(v.ColumnName)} IS NOT DISTINCT FROM ${index + 1}::{byName[v.ColumnName].SqlType})"));
        return prefix + input + $" RETURNING {projection}, ({exact})";
    }
}

internal sealed record PostgresInsertColumn(InsertableDatabaseColumn Capability, uint TypeOid, string? SqlType, int? PrimaryKeyOrder);
