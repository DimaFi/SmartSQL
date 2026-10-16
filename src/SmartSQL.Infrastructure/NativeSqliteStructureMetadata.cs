using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class NativeSqliteDatabaseBrowser
{
    public Task<DatabaseStructure> GetStructureAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        Validate(table);
        return ReadAsync(context =>
        {
            var relation = Relation(context, table);
            PreflightColumns(context, table.Name);
            var columns = Columns(context, relation);
            var budget = new StructureTextBudget();
            budget.Add(table.Schema); budget.Add(table.Name);
            foreach (var column in columns) { budget.Add(column.Name); budget.Add(column.NativeType); }
            var nativeDefinition = SchemaSql(context, table.Kind == DatabaseObjectKind.View ? "view" : "table", table.Name, budget);
            var scope = new DatabaseDiagnostic("sqlite_structure_scope",
                "Показаны только собственные метаданные выбранного объекта main. Входящие внешние ключи, зависимости представлений и ограничения других объектов не проверялись.");

            if (table.Kind == DatabaseObjectKind.View)
            {
                var viewDiagnostics = new DatabaseDiagnostic[]
                {
                    new("sqlite_view_structure", "SQLite-представление не имеет собственных табличных ограничений и индексов в доступной области; показаны выходные столбцы и исходный CREATE VIEW."),
                    scope
                };
                foreach (var diagnostic in viewDiagnostics) { budget.Add(diagnostic.Code); budget.Add(diagnostic.Message); }
                var view = new DatabaseStructure(table, columns, DatabaseMetadataCoverage.Unavailable, [],
                    DatabaseMetadataCoverage.Unavailable, [], nativeDefinition, viewDiagnostics);
                DatabaseStructureLimits.Validate(view);
                return view;
            }

            var indexMetadata = ReadIndexes(context, table.Name, budget);
            var constraints = ReadConstraints(context, table.Name, columns, indexMetadata, budget);
            var diagnostics = new List<DatabaseDiagnostic>
            {
                new("sqlite_constraints_partial", "PK, UNIQUE и исходящие FOREIGN KEY получены из SQLite PRAGMA. Имена ограничений, CHECK и deferrability не восстанавливаются из SQL; полный исходный CREATE TABLE сохранён отдельно."),
                new("sqlite_indexes_partial", "Список индексов и порядок терминов получены из SQLite PRAGMA. Текст отдельного expression-термина, метод и состояние validity/readiness SQLite не сообщает; доступный CREATE INDEX сохранён целиком."),
                scope
            };
            if (indexMetadata.Any(item => item.Index.Terms.Any(term => term.Kind == DatabaseIndexTermKind.Expression)))
                diagnostics.Add(new("sqlite_index_expression", "Expression-термин отмечен явно, но его отдельное выражение не подменяется догадкой; смотрите полный CREATE INDEX."));
            if (constraints.Any(item => item.ForeignKey?.ReferencedColumns.Any(column => column is null) == true))
                diagnostics.Add(new("sqlite_implicit_fk_target", "NULL у столбца назначения FOREIGN KEY означает ссылку на неявно выбранный первичный ключ; имя не было выдумано."));
            foreach (var diagnostic in diagnostics) { budget.Add(diagnostic.Code); budget.Add(diagnostic.Message); }

            var structure = new DatabaseStructure(table, columns, DatabaseMetadataCoverage.Partial, constraints,
                DatabaseMetadataCoverage.Partial, indexMetadata.Select(item => item.Index).ToArray(), nativeDefinition, diagnostics);
            DatabaseStructureLimits.Validate(structure);
            return structure;
        }, cancellationToken, invalidateEditSnapshots: false);
    }

    private static void PreflightColumns(Context context, string tableName)
    {
        // Validate raw text before the shared column reader can perform replacement decoding.
        using var command = context.Command("SELECT CAST(name AS BLOB),length(CAST(name AS BLOB)),CAST(type AS BLOB),length(CAST(type AS BLOB)) FROM pragma_table_xinfo($name,'main') WHERE hidden<>1 LIMIT $limit");
        command.Parameters.AddWithValue("$name", tableName);
        command.Parameters.AddWithValue("$limit", DatabaseStructureLimits.MaxColumns + 1);
        using var reader = command.ExecuteReader();
        var budget = new StructureTextBudget();
        var count = 0;
        while (reader.Read())
        {
            context.Token.ThrowIfCancellationRequested();
            if (++count > DatabaseStructureLimits.MaxColumns) StructureTooLarge();
            budget.Add(BoundedText(context, reader, 0, 1));
            budget.Add(BoundedText(context, reader, 2, 3));
        }
    }

    private static IReadOnlyList<SqliteIndexMetadata> ReadIndexes(Context context, string tableName,
        StructureTextBudget budget)
    {
        var descriptors = new List<(string Name, bool Unique, string Origin, bool Partial)>();
        using (var command = context.Command("SELECT CAST(name AS BLOB),length(CAST(name AS BLOB)),\"unique\",CAST(origin AS BLOB),length(CAST(origin AS BLOB)),partial FROM pragma_index_list($name,'main') ORDER BY name COLLATE BINARY"))
        {
            command.Parameters.AddWithValue("$name", tableName);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                context.Token.ThrowIfCancellationRequested();
                if (descriptors.Count == DatabaseStructureLimits.MaxItems) StructureTooLarge();
                var name = BoundedText(context, reader, 0, 1);
                budget.Add(name);
                descriptors.Add((name, reader.GetInt64(2) != 0,
                    BoundedText(context, reader, 3, 4), reader.GetInt64(5) != 0));
            }
        }
        var indexes = descriptors.Select(item =>
        {
            return new SqliteIndexMetadata(new(item.Name, item.Unique, item.Origin == "pk", item.Partial,
                ReadIndexTerms(context, item.Name, budget), SchemaSql(context, "index", item.Name, budget),
                null, null, null), item.Origin);
        }).ToArray();
        return indexes;
    }

    private static IReadOnlyList<DatabaseIndexTerm> ReadIndexTerms(Context context, string indexName,
        StructureTextBudget budget)
    {
        using var command = context.Command("SELECT seqno,cid,CAST(name AS BLOB),length(CAST(name AS BLOB)),\"desc\",CAST(coll AS BLOB),length(CAST(coll AS BLOB)),key FROM pragma_index_xinfo($name,'main') ORDER BY seqno");
        command.Parameters.AddWithValue("$name", indexName);
        using var reader = command.ExecuteReader();
        var terms = new List<DatabaseIndexTerm>();
        while (reader.Read())
        {
            context.Token.ThrowIfCancellationRequested();
            if (terms.Count == DatabaseStructureLimits.MaxTerms) StructureTooLarge();
            var cid = reader.GetInt64(1);
            var name = reader.IsDBNull(2) ? null : BoundedText(context, reader, 2, 3);
            var kind = cid switch
            {
                >= 0 when name is not null => DatabaseIndexTermKind.Column,
                -2 => DatabaseIndexTermKind.Expression,
                -1 => DatabaseIndexTermKind.RowIdentifier,
                _ => DatabaseIndexTermKind.Other
            };
            var key = reader.GetInt64(7) != 0;
            var columnName = kind == DatabaseIndexTermKind.Column ? name : null;
            var collation = key && !reader.IsDBNull(5) ? BoundedText(context, reader, 5, 6) : null;
            budget.Add(columnName); budget.Add(collation);
            terms.Add(new(checked(reader.GetInt32(0) + 1), kind, columnName, null, key,
                key ? reader.GetInt64(4) != 0 : null, collation));
        }
        return terms;
    }

    private static IReadOnlyList<DatabaseConstraint> ReadConstraints(Context context, string tableName,
        IReadOnlyList<DatabaseColumn> columns, IReadOnlyList<SqliteIndexMetadata> indexes,
        StructureTextBudget budget)
    {
        var constraints = new List<DatabaseConstraint>();
        var primary = columns.Where(column => column.PrimaryKeyOrder is not null)
            .OrderBy(column => column.PrimaryKeyOrder).Select(column => column.Name).ToArray();
        if (primary.Length > 0)
            AddConstraint(constraints, new(null, DatabaseConstraintKind.PrimaryKey, "PRIMARY KEY", primary));

        foreach (var item in indexes.Where(item => item.Origin == "u"))
        {
            var keyTerms = item.Index.Terms.Where(term => term.IsKey).ToArray();
            var uniqueColumns = keyTerms.Where(term => term.Kind == DatabaseIndexTermKind.Column && term.ColumnName is not null)
                .Select(term => term.ColumnName!).ToArray();
            AddConstraint(constraints, new(null, DatabaseConstraintKind.Unique, "UNIQUE", uniqueColumns));
        }

        foreach (var foreign in ReadForeignKeys(context, tableName))
            AddConstraint(constraints, foreign);
        foreach (var constraint in constraints)
        {
            budget.Add(constraint.Name); budget.Add(constraint.NativeKind); budget.Add(constraint.NativeDefinition);
            foreach (var column in constraint.Columns) budget.Add(column);
            if (constraint.ForeignKey is not { } foreign) continue;
            budget.Add(foreign.ReferencedSchema); budget.Add(foreign.ReferencedTable);
            budget.Add(foreign.OnUpdate); budget.Add(foreign.OnDelete); budget.Add(foreign.Match);
            foreach (var column in foreign.ReferencedColumns) budget.Add(column);
        }
        return constraints;
    }

    private static IReadOnlyList<DatabaseConstraint> ReadForeignKeys(Context context, string tableName)
    {
        using var command = context.Command("SELECT id,seq,CAST(\"table\" AS BLOB),length(CAST(\"table\" AS BLOB)),CAST(\"from\" AS BLOB),length(CAST(\"from\" AS BLOB)),CAST(\"to\" AS BLOB),length(CAST(\"to\" AS BLOB)),CAST(on_update AS BLOB),length(CAST(on_update AS BLOB)),CAST(on_delete AS BLOB),length(CAST(on_delete AS BLOB)),CAST(match AS BLOB),length(CAST(match AS BLOB)) FROM pragma_foreign_key_list($name,'main') ORDER BY id,seq");
        command.Parameters.AddWithValue("$name", tableName);
        using var reader = command.ExecuteReader();
        var builders = new List<SqliteForeignKeyBuilder>();
        var budget = new StructureTextBudget();
        SqliteForeignKeyBuilder? current = null;
        while (reader.Read())
        {
            context.Token.ThrowIfCancellationRequested();
            var id = reader.GetInt32(0);
            if (current?.Id != id)
            {
                if (builders.Count == DatabaseStructureLimits.MaxItems) StructureTooLarge();
                current = new(id, BoundedText(context, reader, 2, 3), BoundedText(context, reader, 8, 9),
                    BoundedText(context, reader, 10, 11), BoundedText(context, reader, 12, 13));
                budget.Add(current.ReferencedTable); budget.Add(current.OnUpdate);
                budget.Add(current.OnDelete); budget.Add(current.Match);
                builders.Add(current);
            }
            if (current.Columns.Count == DatabaseStructureLimits.MaxTerms) StructureTooLarge();
            var source = BoundedText(context, reader, 4, 5);
            var target = reader.IsDBNull(6) ? null : BoundedText(context, reader, 6, 7);
            budget.Add(source); budget.Add(target);
            current.Columns.Add((reader.GetInt32(1), source, target));
        }
        return builders.Select(builder =>
        {
            var ordered = builder.Columns.OrderBy(column => column.Sequence).ToArray();
            return new DatabaseConstraint(null, DatabaseConstraintKind.ForeignKey, "FOREIGN KEY",
                ordered.Select(column => column.Source).ToArray(), null,
                new("main", builder.ReferencedTable, ordered.Select(column => column.Target).ToArray(),
                    builder.OnUpdate, builder.OnDelete, builder.Match));
        }).ToArray();
    }

    private static string? SchemaSql(Context context, string type, string name, StructureTextBudget budget)
    {
        using var command = context.Command("SELECT length(CAST(sql AS BLOB)),CAST(sql AS BLOB) FROM main.sqlite_schema WHERE type=$type AND name=$name");
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(1)) return null;
        if (!reader.IsDBNull(0) && reader.GetInt64(0) > context.TextEncoding.GetMaxByteCount(DatabaseStructureLimits.MaxTextCharacters)) StructureTooLarge();
        var sql = Decode((byte[])reader.GetValue(1), context.TextEncoding);
        if (sql.Length > DatabaseStructureLimits.MaxTextCharacters) StructureTooLarge();
        budget.Add(sql);
        return sql;
    }

    private static string BoundedText(Context context, SqliteDataReader reader, int ordinal, int byteLengthOrdinal)
    {
        if (reader.GetInt64(byteLengthOrdinal) > context.TextEncoding.GetMaxByteCount(DatabaseStructureLimits.MaxTextCharacters))
            StructureTooLarge();
        var value = Decode((byte[])reader.GetValue(ordinal), context.TextEncoding);
        if (value.Length > DatabaseStructureLimits.MaxTextCharacters) StructureTooLarge();
        return value;
    }

    private static void AddConstraint(List<DatabaseConstraint> constraints, DatabaseConstraint constraint)
    {
        if (constraints.Count == DatabaseStructureLimits.MaxItems) StructureTooLarge();
        constraints.Add(constraint);
    }

    private static void StructureTooLarge() => throw new DatabaseBrowserException("structure_too_large",
        "Структура объекта превышает допустимый размер. Частичные метаданные не показаны.");

    private sealed record SqliteIndexMetadata(DatabaseIndex Index, string Origin);
    private sealed class StructureTextBudget
    {
        private long _characters;
        internal void Add(string? value)
        {
            if (value is not null && (value.Length > DatabaseStructureLimits.MaxTextCharacters
                || (_characters += value.Length) > DatabaseStructureLimits.MaxTotalCharacters))
                StructureTooLarge();
        }
    }
    private sealed class SqliteForeignKeyBuilder(int id, string referencedTable, string onUpdate,
        string onDelete, string match)
    {
        internal int Id { get; } = id;
        internal string ReferencedTable { get; } = referencedTable;
        internal string OnUpdate { get; } = onUpdate;
        internal string OnDelete { get; } = onDelete;
        internal string Match { get; } = match;
        internal List<(int Sequence, string Source, string? Target)> Columns { get; } = [];
    }
}
