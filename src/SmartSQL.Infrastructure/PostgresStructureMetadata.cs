using System.Data;
using Npgsql;
using NpgsqlTypes;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class PostgresDatabaseBrowser
{
    /// <summary>Object-owned catalog metadata; this read never replaces current row-operation handles.</summary>
    public async Task<DatabaseStructure> GetStructureAsync(DatabaseTableRef table, CancellationToken cancellationToken = default)
    {
        ValidateTable(table);
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(async (connection, transaction, token) =>
            {
                await LockQueryRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
                var relation = await ReadRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
                await CheckStructureCountsAsync(relation.Id, connection, transaction, token).ConfigureAwait(false);
                await CheckStructureTextSizesAsync(relation.Id, connection, transaction, token).ConfigureAwait(false);
                var columns = await ReadColumnsAsync(relation.Id, connection, transaction, token).ConfigureAwait(false);
                var budget = new StructureTextBudget();
                budget.Add(relation.Table.Schema); budget.Add(relation.Table.Name);
                foreach (var column in columns) { budget.Add(column.Name); budget.Add(column.NativeType); }
                var diagnostics = new List<DatabaseDiagnostic>
                {
                    new("structure_owned_scope", "Показаны ограничения и индексы, принадлежащие выбранному объекту. Входящие внешние ключи и ограничения доменных типов этим срезом не исследуются. NOT NULL отражён в свойствах столбцов; отдельные native NOT NULL constraints показаны, если сервер хранит их в pg_constraint."),
                    new("structure_not_write_permission", "Объявленные ключи и индексы не подтверждают возможность записи. Права и безопасные операции проверяются отдельно.")
                };
                var constraints = await ReadStructureConstraintsAsync(relation.Id, connection, transaction, budget, token).ConfigureAwait(false);
                var indexes = await ReadStructureIndexesAsync(relation.Id, connection, transaction, budget, token).ConfigureAwait(false);
                string? nativeDefinition = null;
                if (relation.Table.Kind is DatabaseObjectKind.View or DatabaseObjectKind.MaterializedView)
                {
                    await using var definition = StructureCommand("SELECT pg_catalog.pg_get_viewdef($1::pg_catalog.oid, true)", relation.Id, connection, transaction);
                    await using var reader = await definition.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
                    if (await reader.ReadAsync(token).ConfigureAwait(false)) nativeDefinition = await budget.ReadAsync(reader, 0, token).ConfigureAwait(false);
                }
                else diagnostics.Add(new("table_definition_unavailable", "Полный CREATE TABLE DDL не реконструируется. Native definitions отдельных ограничений и индексов показаны без преобразования."));
                var constraintCoverage = constraints.Any(item => item.Kind == DatabaseConstraintKind.Other || item.Kind == DatabaseConstraintKind.ForeignKey && item.ForeignKey is null || item.Columns.Any(string.IsNullOrEmpty)
                    || item.ForeignKey is { } foreign && (foreign.ReferencedColumns.Any(string.IsNullOrEmpty) || foreign.ReferencedColumns.Count != item.Columns.Count))
                    ? DatabaseMetadataCoverage.Partial : DatabaseMetadataCoverage.Complete;
                if (constraintCoverage == DatabaseMetadataCoverage.Partial)
                    diagnostics.Add(new("constraint_details_partial", "Есть неизвестный native вид ограничения или неподтверждённое сопоставление столбцов. Исходные определения сохранены; полнота структурированной интерпретации не заявляется."));
                var indexCoverage = indexes.Any(index => index.Terms.Any(term => term.Kind == DatabaseIndexTermKind.Other))
                    ? DatabaseMetadataCoverage.Partial : DatabaseMetadataCoverage.Complete;
                if (indexCoverage == DatabaseMetadataCoverage.Partial)
                    diagnostics.Add(new("index_details_partial", "Есть индексный элемент без подтверждённого столбца/выражения. Native definition сохранено; структурированные подробности неполны."));
                var result = new DatabaseStructure(relation.Table, columns, constraintCoverage, constraints,
                    indexCoverage, indexes, nativeDefinition, diagnostics);
                DatabaseStructureLimits.Validate(result);
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _editGate.Release(); }
    }

    private const string StructureCounts = """
        SELECT (SELECT count(*) FROM pg_catalog.pg_attribute WHERE attrelid=$1 AND attnum>0 AND NOT attisdropped),
               (SELECT count(*) FROM pg_catalog.pg_constraint WHERE conrelid=$1),
               (SELECT count(*) FROM pg_catalog.pg_index WHERE indrelid=$1),
               (SELECT COALESCE(max(GREATEST(COALESCE(pg_catalog.cardinality(conkey),0),COALESCE(pg_catalog.cardinality(confkey),0))),0) FROM pg_catalog.pg_constraint WHERE conrelid=$1),
               (SELECT COALESCE(max(indnatts),0)::pg_catalog.int4 FROM pg_catalog.pg_index WHERE indrelid=$1)
        """;

    private static async Task CheckStructureCountsAsync(uint relation, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = StructureCommand(StructureCounts, relation, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)
            || reader.GetInt64(0) > DatabaseStructureLimits.MaxColumns
            || reader.GetInt64(1) > DatabaseStructureLimits.MaxItems || reader.GetInt64(2) > DatabaseStructureLimits.MaxItems
            || reader.GetInt32(3) > DatabaseStructureLimits.MaxTerms || reader.GetInt32(4) > DatabaseStructureLimits.MaxTerms)
            throw StructureTooLarge();
    }

    // Check generated native text on the server before transporting any full definitions.
    // Bounded streaming and the incremental UTF-16 budget below remain authoritative.
    private const string StructureTextSizes = """
        WITH native_text(value) AS (
          SELECT a.attname::pg_catalog.text FROM pg_catalog.pg_attribute a WHERE a.attrelid=$1 AND a.attnum>0 AND NOT a.attisdropped
          UNION ALL
          SELECT pg_catalog.format_type(a.atttypid,a.atttypmod) FROM pg_catalog.pg_attribute a WHERE a.attrelid=$1 AND a.attnum>0 AND NOT a.attisdropped
          UNION ALL
          SELECT k.conname::pg_catalog.text FROM pg_catalog.pg_constraint k WHERE k.conrelid=$1
          UNION ALL
          SELECT pg_catalog.pg_get_constraintdef(k.oid,true) FROM pg_catalog.pg_constraint k WHERE k.conrelid=$1
          UNION ALL
          SELECT c.relname::pg_catalog.text FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid=i.indexrelid WHERE i.indrelid=$1
          UNION ALL
          SELECT pg_catalog.pg_get_indexdef(i.indexrelid) FROM pg_catalog.pg_index i WHERE i.indrelid=$1
          UNION ALL
          SELECT pg_catalog.pg_get_indexdef(i.indexrelid,p.position,true) FROM pg_catalog.pg_index i
            CROSS JOIN LATERAL pg_catalog.generate_series(1,i.indnatts::pg_catalog.int4) p(position) WHERE i.indrelid=$1
          UNION ALL
          SELECT CASE WHEN c.relkind IN ('v','m') THEN pg_catalog.pg_get_viewdef(c.oid,true) ELSE NULL END FROM pg_catalog.pg_class c WHERE c.oid=$1
        )
        SELECT COALESCE(max(pg_catalog.char_length(value)),0),COALESCE(sum(pg_catalog.char_length(value)),0) FROM native_text
        """;

    private static async Task CheckStructureTextSizesAsync(uint relation, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = StructureCommand(StructureTextSizes, relation, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(0) > DatabaseStructureLimits.MaxTextCharacters
            || reader.GetInt64(1) > DatabaseStructureLimits.MaxTotalCharacters) throw StructureTooLarge();
    }

    private const string StructureConstraints = """
        SELECT k.conname::pg_catalog.text, k.contype::pg_catalog.text,
               ARRAY(SELECT COALESCE(a.attname::pg_catalog.text,'') FROM pg_catalog.unnest(k.conkey) WITH ORDINALITY p(attnum,position)
                     LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid=k.conrelid AND a.attnum=p.attnum AND NOT a.attisdropped ORDER BY p.position),
               pg_catalog.pg_get_constraintdef(k.oid,true), k.condeferrable, k.condeferred, k.convalidated,
               n.nspname::pg_catalog.text, r.relname::pg_catalog.text,
               ARRAY(SELECT a.attname::pg_catalog.text FROM pg_catalog.unnest(k.confkey) WITH ORDINALITY p(attnum,position)
                     LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid=k.confrelid AND a.attnum=p.attnum AND NOT a.attisdropped ORDER BY p.position),
               k.confupdtype::pg_catalog.text, k.confdeltype::pg_catalog.text, k.confmatchtype::pg_catalog.text
        FROM pg_catalog.pg_constraint k
        LEFT JOIN pg_catalog.pg_class r ON r.oid=k.confrelid
        LEFT JOIN pg_catalog.pg_namespace n ON n.oid=r.relnamespace
        WHERE k.conrelid=$1 ORDER BY k.conname COLLATE pg_catalog."C",k.oid
        LIMIT 2001
        """;

    private static async Task<IReadOnlyList<DatabaseConstraint>> ReadStructureConstraintsAsync(uint relation, NpgsqlConnection connection,
        NpgsqlTransaction transaction, StructureTextBudget budget, CancellationToken token)
    {
        await using var command = StructureCommand(StructureConstraints, relation, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
        var constraints = new List<DatabaseConstraint>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (constraints.Count == DatabaseStructureLimits.MaxItems) throw StructureTooLarge();
            var name = budget.Add(reader.GetString(0))!;
            var nativeKind = budget.Add(reader.GetString(1))!;
            var sourceColumns = reader.GetFieldValue<string[]>(2);
            foreach (var column in sourceColumns) budget.Add(column);
            var definition = await budget.ReadAsync(reader, 3, token).ConfigureAwait(false);
            var deferrable = reader.GetBoolean(4); var deferred = reader.GetBoolean(5); var validated = reader.GetBoolean(6);
            var referencedSchema = reader.IsDBNull(7) ? null : budget.Add(reader.GetString(7));
            var referencedTable = reader.IsDBNull(8) ? null : budget.Add(reader.GetString(8));
            var referencedColumns = reader.GetFieldValue<string?[]>(9);
            foreach (var column in referencedColumns) budget.Add(column);
            var update = reader.GetString(10); var delete = reader.GetString(11); var match = reader.GetString(12);
            var kind = nativeKind switch
            {
                "p" => DatabaseConstraintKind.PrimaryKey, "u" => DatabaseConstraintKind.Unique, "f" => DatabaseConstraintKind.ForeignKey,
                "c" => DatabaseConstraintKind.Check, "x" => DatabaseConstraintKind.Exclusion, "n" => DatabaseConstraintKind.NotNull,
                _ => DatabaseConstraintKind.Other
            };
            DatabaseForeignKey? foreign = nativeKind == "f" && referencedTable is not null
                ? new(referencedSchema, referencedTable, referencedColumns, StructureForeignAction(update), StructureForeignAction(delete),
                    match switch { "f" => "FULL", "p" => "PARTIAL", "s" => "SIMPLE", _ => match }) : null;
            constraints.Add(new(name, kind, nativeKind, sourceColumns, definition, foreign, deferrable, deferred, validated));
        }
        return constraints;
    }

    private const string StructureIndexes = """
        SELECT i.indexrelid, c.relname::pg_catalog.text, i.indisunique, i.indisprimary,
               i.indpred IS NOT NULL, pg_catalog.pg_get_indexdef(i.indexrelid),
               am.amname::pg_catalog.text, i.indisvalid, i.indisready
        FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid=i.indexrelid
        JOIN pg_catalog.pg_am am ON am.oid=c.relam
        WHERE i.indrelid=$1 ORDER BY c.relname COLLATE pg_catalog."C",i.indexrelid LIMIT 2001
        """;

    private const string StructureIndexTerms = """
        SELECT p.position::pg_catalog.int4, p.attnum::pg_catalog.int4, a.attname::pg_catalog.text,
               pg_catalog.pg_get_indexdef(i.indexrelid,p.position::pg_catalog.int4,true),
               p.position<=i.indnkeyatts,
               CASE WHEN am.amname='btree' AND p.position<=i.indnkeyatts THEN (i.indoption[(p.position-1)::pg_catalog.int4] & 1)=1 ELSE NULL END,
               CASE WHEN col.oid IS NOT NULL THEN pg_catalog.quote_ident(ns.nspname)||'.'||pg_catalog.quote_ident(col.collname) ELSE NULL END
        FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class ix ON ix.oid=i.indexrelid
        JOIN pg_catalog.pg_am am ON am.oid=ix.relam
        CROSS JOIN LATERAL pg_catalog.unnest(i.indkey) WITH ORDINALITY p(attnum,position)
        LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=p.attnum AND NOT a.attisdropped
        LEFT JOIN pg_catalog.pg_collation col ON col.oid=i.indcollation[(p.position-1)::pg_catalog.int4]
        LEFT JOIN pg_catalog.pg_namespace ns ON ns.oid=col.collnamespace
        WHERE i.indexrelid=$1 ORDER BY p.position LIMIT 4097
        """;

    private static async Task<IReadOnlyList<DatabaseIndex>> ReadStructureIndexesAsync(uint relation, NpgsqlConnection connection,
        NpgsqlTransaction transaction, StructureTextBudget budget, CancellationToken token)
    {
        var indexes = new List<(uint Id, DatabaseIndex Index)>();
        await using (var command = StructureCommand(StructureIndexes, relation, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (indexes.Count == DatabaseStructureLimits.MaxItems) throw StructureTooLarge();
                var id = reader.GetFieldValue<uint>(0); var name = budget.Add(reader.GetString(1))!;
                var unique = reader.GetBoolean(2); var primary = reader.GetBoolean(3); var partial = reader.GetBoolean(4);
                var definition = await budget.ReadAsync(reader, 5, token).ConfigureAwait(false);
                var method = budget.Add(reader.GetString(6)); var valid = reader.GetBoolean(7); var ready = reader.GetBoolean(8);
                indexes.Add((id, new(name, unique, primary, partial, [], definition, method, valid, ready)));
            }
        }
        var result = new List<DatabaseIndex>(indexes.Count);
        foreach (var (id, index) in indexes)
        {
            token.ThrowIfCancellationRequested();
            await using var command = StructureCommand(StructureIndexTerms, id, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
            var terms = new List<DatabaseIndexTerm>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (terms.Count == DatabaseStructureLimits.MaxTerms) throw StructureTooLarge();
                var ordinal = reader.GetInt32(0); var attribute = reader.GetInt32(1);
                var name = reader.IsDBNull(2) ? null : budget.Add(reader.GetString(2));
                var definition = await budget.ReadAsync(reader, 3, token).ConfigureAwait(false);
                var key = reader.GetBoolean(4); var descending = reader.IsDBNull(5) ? (bool?)null : reader.GetBoolean(5);
                var collation = reader.IsDBNull(6) ? null : budget.Add(reader.GetString(6));
                var kind = attribute == 0 ? DatabaseIndexTermKind.Expression : attribute > 0 && name is not null
                    ? DatabaseIndexTermKind.Column : DatabaseIndexTermKind.Other;
                terms.Add(new(ordinal, kind, name, definition, key, descending, collation));
            }
            result.Add(index with { Terms = terms });
        }
        return result;
    }

    private static NpgsqlCommand StructureCommand(string sql, uint oid, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var command = Command(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = oid });
        return command;
    }

    private static string StructureForeignAction(string action) => action switch
    {
        "a" => "NO ACTION", "r" => "RESTRICT", "c" => "CASCADE", "n" => "SET NULL", "d" => "SET DEFAULT", _ => action
    };

    private static DatabaseBrowserException StructureTooLarge() => new("structure_too_large",
        "Структура объекта превышает допустимый размер. Частичные метаданные не показаны.");

    private sealed class StructureTextBudget
    {
        private long _characters;
        internal string? Add(string? text)
        {
            if (text is not null && (text.Length > DatabaseStructureLimits.MaxTextCharacters
                || (_characters += text.Length) > DatabaseStructureLimits.MaxTotalCharacters)) throw StructureTooLarge();
            return text;
        }
        internal async Task<string?> ReadAsync(NpgsqlDataReader reader, int ordinal, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false)) return null;
            using var text = await reader.GetTextReaderAsync(ordinal, token).ConfigureAwait(false);
            try
            {
                var maximum = (int)Math.Min(DatabaseStructureLimits.MaxTextCharacters,
                    DatabaseStructureLimits.MaxTotalCharacters - _characters);
                return Add(await ReadTextAsync(text, maximum, token).ConfigureAwait(false));
            }
            catch (DatabaseBrowserException exception) when (exception.Code == "cell_too_large") { throw StructureTooLarge(); }
        }
    }
}
