using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

internal static class PostgresEditingSql
{
    internal const string TableSafety = """
        WITH protective_lock AS (
          SELECT c.oid,
                 (CASE WHEN pg_catalog.current_setting('server_version_num')::pg_catalog.int4>=170000
                   THEN pg_catalog.has_table_privilege(c.oid,'MAINTAIN') ELSE false END
                  OR pg_catalog.has_table_privilege(c.oid,'UPDATE,DELETE,TRUNCATE')) AS allowed
          FROM pg_catalog.pg_class c WHERE c.oid=$1),
        expressions(classid,objid,node) AS (
          SELECT 'pg_catalog.pg_constraint'::pg_catalog.regclass,k.oid,k.conbin::pg_catalog.text
            FROM pg_catalog.pg_constraint k WHERE k.conrelid=$1 AND k.contype='c'
          UNION ALL
          SELECT 'pg_catalog.pg_attrdef'::pg_catalog.regclass,d.oid,d.adbin::pg_catalog.text
            FROM pg_catalog.pg_attrdef d JOIN pg_catalog.pg_attribute a ON a.attrelid=d.adrelid AND a.attnum=d.adnum
            WHERE d.adrelid=$1 AND a.attgenerated<>''
          UNION ALL
          SELECT 'pg_catalog.pg_class'::pg_catalog.regclass,i.indexrelid,
                 COALESCE(i.indexprs::pg_catalog.text,'') || ' ' || COALESCE(i.indpred::pg_catalog.text,'')
            FROM pg_catalog.pg_index i WHERE i.indrelid=$1)
        SELECT c.relkind = 'r' AND NOT c.relispartition
               AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
               AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits h WHERE h.inhrelid=c.oid OR h.inhparent=c.oid)
               AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid=c.oid AND NOT t.tgisinternal)
               AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid AND r.ev_type='2')
               AND NOT EXISTS (SELECT 1 FROM expressions x
                    JOIN pg_catalog.pg_depend d ON d.classid=x.classid AND d.objid=x.objid
                    LEFT JOIN pg_catalog.pg_operator o ON d.refclassid='pg_catalog.pg_operator'::pg_catalog.regclass AND o.oid=d.refobjid
                    JOIN pg_catalog.pg_proc p ON p.oid=CASE WHEN d.refclassid='pg_catalog.pg_proc'::pg_catalog.regclass THEN d.refobjid ELSE o.oprcode END
                    WHERE p.pronamespace <> 'pg_catalog'::pg_catalog.regnamespace OR p.provolatile='v'
                         OR o.oprnamespace <> 'pg_catalog'::pg_catalog.regnamespace)
               -- Pinned builtin functions/operators have no pg_depend rows. Inspect the
               -- catalog node IDs too, rather than accepting volatile builtin CHECKs.
               AND NOT EXISTS (SELECT 1 FROM expressions x
                    CROSS JOIN LATERAL pg_catalog.regexp_matches(x.node,':(funcid|opfuncid|opno) ([0-9]+)','g') m(parts)
                    LEFT JOIN pg_catalog.pg_operator o ON m.parts[1]='opno' AND o.oid=m.parts[2]::pg_catalog.oid
                    JOIN pg_catalog.pg_proc p ON p.oid=CASE WHEN m.parts[1]='opno' THEN o.oprcode::pg_catalog.oid ELSE m.parts[2]::pg_catalog.oid END
                    WHERE p.pronamespace <> 'pg_catalog'::pg_catalog.regnamespace OR p.provolatile='v'
                         OR o.oprnamespace <> 'pg_catalog'::pg_catalog.regnamespace)
               AND NOT EXISTS (SELECT 1 FROM expressions x
                    JOIN pg_catalog.pg_depend d ON d.classid=x.classid AND d.objid=x.objid AND d.refclassid='pg_catalog.pg_type'::pg_catalog.regclass
                    JOIN pg_catalog.pg_type t ON t.oid=d.refobjid
                    WHERE t.typnamespace <> 'pg_catalog'::pg_catalog.regnamespace)
               AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i CROSS JOIN LATERAL pg_catalog.unnest(i.indclass) x(oid)
                    JOIN pg_catalog.pg_opclass o ON o.oid=x.oid
                    WHERE i.indrelid=c.oid AND o.opcnamespace <> 'pg_catalog'::pg_catalog.regnamespace)
               AND NOT EXISTS (
                 WITH RECURSIVE types(oid) AS (
                   SELECT a.atttypid FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
                   UNION
                   SELECT edge.oid FROM types x JOIN pg_catalog.pg_type t ON t.oid=x.oid
                     CROSS JOIN LATERAL (
                       SELECT t.typbasetype AS oid WHERE t.typbasetype<>0
                       UNION SELECT t.typelem WHERE t.typelem<>0
                       UNION SELECT r.rngsubtype FROM pg_catalog.pg_range r WHERE r.rngtypid=t.oid OR r.rngmultitypid=t.oid
                       UNION SELECT a.atttypid FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.typrelid AND a.attnum>0 AND NOT a.attisdropped
                     ) edge)
                 SELECT 1 FROM types x JOIN pg_catalog.pg_type t ON t.oid=x.oid JOIN pg_catalog.pg_proc p ON p.oid=t.typoutput
                 WHERE p.pronamespace <> 'pg_catalog'::pg_catalog.regnamespace OR p.provolatile='v'
                   OR EXISTS(SELECT 1 FROM pg_catalog.pg_cast z JOIN pg_catalog.pg_proc f ON f.oid=z.castfunc
                      WHERE z.castsource=t.oid AND z.casttarget=25 AND (f.pronamespace <> 'pg_catalog'::pg_catalog.regnamespace OR f.provolatile='v')))
               AND l.allowed
               AND pg_catalog.has_table_privilege(c.oid,'SELECT') AND pg_catalog.has_table_privilege(c.oid,'UPDATE'),
               pg_catalog.jsonb_build_object(
                 'relation', pg_catalog.jsonb_build_array(c.oid,c.relkind,c.relispartition,c.relrowsecurity,c.relforcerowsecurity),
                 'columns', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(a.attnum,a.attname,a.atttypid,a.atttypmod,a.attnotnull,a.attidentity,a.attgenerated,a.attcollation) ORDER BY a.attnum)
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
                 'defaults', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(d.oid,d.adnum,d.adbin::pg_catalog.text,pg_catalog.pg_get_expr(d.adbin,d.adrelid)) ORDER BY d.adnum)
                    FROM pg_catalog.pg_attrdef d JOIN pg_catalog.pg_attribute a ON a.attrelid=d.adrelid AND a.attnum=d.adnum
                    WHERE d.adrelid=c.oid),
                 'constraints', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(p.oid,p.contype,p.convalidated,p.condeferrable,pg_catalog.pg_get_constraintdef(p.oid)) ORDER BY p.oid)
                    FROM pg_catalog.pg_constraint p WHERE p.conrelid=c.oid),
                 'indexes', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(i.indexrelid,i.indisvalid,i.indisready,i.indislive,i.indimmediate,i.indclass::pg_catalog.text,i.indcollation::pg_catalog.text,pg_catalog.pg_get_indexdef(i.indexrelid)) ORDER BY i.indexrelid)
                    FROM pg_catalog.pg_index i WHERE i.indrelid=c.oid),
                 'triggers', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(t.oid,t.tgenabled,pg_catalog.pg_get_triggerdef(t.oid)) ORDER BY t.oid)
                    FROM pg_catalog.pg_trigger t WHERE t.tgrelid=c.oid),
                 'rules', (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_array(r.oid,pg_catalog.pg_get_ruledef(r.oid)) ORDER BY r.oid)
                    FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))::pg_catalog.text,
               l.allowed
        FROM pg_catalog.pg_class c JOIN protective_lock l ON l.oid=c.oid WHERE c.oid=$1
        """;

    internal const string Columns = """
        SELECT a.attname::pg_catalog.text, pg_catalog.format_type(a.atttypid,a.atttypmod),
               NOT a.attnotnull AND NOT t.typnotnull, a.atttypid, a.atttypmod,
               a.attidentity <> '' OR a.attgenerated <> '',
               EXISTS(SELECT 1 FROM pg_catalog.pg_index u WHERE u.indrelid=a.attrelid AND u.indisunique
                    AND (a.attnum=ANY(u.indkey) OR EXISTS(SELECT 1 FROM pg_catalog.pg_depend d
                         WHERE d.classid='pg_catalog.pg_class'::pg_catalog.regclass AND d.objid=u.indexrelid
                           AND d.refclassid='pg_catalog.pg_class'::pg_catalog.regclass AND d.refobjid=a.attrelid AND d.refobjsubid=a.attnum))),
               CASE WHEN i.indexrelid IS NOT NULL THEN pg_catalog.array_position(pk.conkey,a.attnum) ELSE NULL END
        FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_type t ON t.oid=a.atttypid
        LEFT JOIN pg_catalog.pg_constraint pk ON pk.conrelid=a.attrelid AND pk.contype='p' AND pk.convalidated AND NOT pk.condeferrable
        LEFT JOIN pg_catalog.pg_index i ON i.indexrelid=pk.conindid AND i.indisvalid AND i.indisunique AND i.indisready AND i.indislive
             AND i.indimmediate AND i.indpred IS NULL AND i.indexprs IS NULL
             AND i.indnkeyatts=pg_catalog.cardinality(pk.conkey)
             AND NOT EXISTS (SELECT 1 FROM pg_catalog.unnest(i.indclass) x(oid)
                 JOIN pg_catalog.pg_opclass o ON o.oid=x.oid
                 JOIN pg_catalog.pg_am m ON m.oid=o.opcmethod
                 WHERE o.opcnamespace <> 'pg_catalog'::pg_catalog.regnamespace OR NOT o.opcdefault OR m.amname <> 'btree')
        WHERE a.attrelid=$1 AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum
        """;

    internal static string Qualified(DatabaseTableRef table) => $"{PostgresBrowserSql.QuoteIdentifier(table.Schema)}.{PostgresBrowserSql.QuoteIdentifier(table.Name)}";

    internal static string Projection(IReadOnlyList<PostgresEditColumn> columns) => string.Join(", ", columns.Select(c => $"q.{PostgresBrowserSql.QuoteIdentifier(c.Column.Name)}::pg_catalog.text"));

    internal static string KeyPredicate(IReadOnlyList<PostgresEditColumn> keys, int startParameter = 1) =>
        string.Join(" AND ", keys.Select((key, index) => $"q.{PostgresBrowserSql.QuoteIdentifier(key.Column.Name)} IS NOT DISTINCT FROM ${startParameter + index}::{key.SqlType}"));

    internal static string LockRow(DatabaseTableRef table, IReadOnlyList<PostgresEditColumn> columns, IReadOnlyList<PostgresEditColumn> keys) =>
        $"SELECT {Projection(columns)}, q.xmin::pg_catalog.text FROM ONLY {Qualified(table)} AS q WHERE {KeyPredicate(keys)} FOR UPDATE";

    internal static string Update(DatabaseTableRef table, IReadOnlyList<PostgresEditColumn> columns,
        IReadOnlyList<PostgresEditColumn> keys, PostgresEditColumn target)
    {
        var name = PostgresBrowserSql.QuoteIdentifier(target.Column.Name);
        var valueParameter = keys.Count + 1;
        // Never cast to a varchar/numeric typmod: assignment must fail rather than truncate text.
        return $"UPDATE ONLY {Qualified(table)} AS q SET {name}=${valueParameter}::{target.SqlType} WHERE {KeyPredicate(keys)} "
            + $"RETURNING {Projection(columns)}, (q.{name} IS NOT DISTINCT FROM ${valueParameter}::{target.SqlType})";
    }

    internal static (DatabaseEditValueKind Kind, string? SqlType) ValueType(uint oid) => oid switch
    {
        25 => (DatabaseEditValueKind.Text, "pg_catalog.text"),
        1043 => (DatabaseEditValueKind.Text, "pg_catalog.varchar"),
        21 => (DatabaseEditValueKind.Integer, "pg_catalog.int2"),
        23 => (DatabaseEditValueKind.Integer, "pg_catalog.int4"),
        20 => (DatabaseEditValueKind.Integer, "pg_catalog.int8"),
        1700 => (DatabaseEditValueKind.Decimal, "pg_catalog.numeric"),
        1082 => (DatabaseEditValueKind.Date, "pg_catalog.date"),
        16 => (DatabaseEditValueKind.Boolean, "pg_catalog.bool"),
        _ => (DatabaseEditValueKind.Text, null)
    };

    internal static string? KeyType(uint oid) => oid == 2950 ? "pg_catalog.uuid" : ValueType(oid).SqlType;
}

internal sealed record PostgresEditColumn(EditableDatabaseColumn Column, uint TypeOid, string? SqlType, int? PrimaryKeyOrder);
