using System.Data;
using Npgsql;
using NpgsqlTypes;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class PostgresDatabaseBrowser
{
    // Exactly one pending insertion confirmation per connection. Page reads do not
    // consume it; a new dialog/capability request replaces the previous confirmation.
    private InsertConfirmation? _insertConfirmation;
    private sealed record InsertMetadata(uint RelationId, string Fingerprint,
        DatabaseInsertCapabilities Capabilities, IReadOnlyList<PostgresInsertColumn> Columns);
    private sealed record InsertConfirmation(Guid Handle, InsertMetadata Metadata, DateTimeOffset CreatedAt);

    private void InvalidateInsertConfirmation() => _insertConfirmation = null;

    public async Task<DatabaseInsertCapabilities> GetInsertCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        ValidateTable(table);
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InvalidateInsertConfirmation();
            var metadata = await ReadAsync(async (connection, transaction, token) =>
            {
                var relation = await ReadRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
                return await ReadInsertMetadataAsync(relation.Id, relation.Table, connection, transaction, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            if (!metadata.Capabilities.CanInsert) return metadata.Capabilities;
            var handle = Guid.NewGuid();
            _insertConfirmation = new InsertConfirmation(handle, metadata, DateTimeOffset.UtcNow);
            return metadata.Capabilities with { ConfirmationHandle = handle };
        }
        finally { _editGate.Release(); }
    }

    private async Task<InsertMetadata> ReadInsertMetadataAsync(uint relationId, DatabaseTableRef table,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        bool safe;
        bool canProtectSchema;
        string fingerprint;
        await using (var command = Command(PostgresInsertionSql.TableSafety, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relationId });
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw InsertSchemaChanged();
            safe = reader.GetBoolean(0) && table.Kind == DatabaseObjectKind.Table;
            using var metadataText = await reader.GetTextReaderAsync(1, token).ConfigureAwait(false);
            try { fingerprint = await ReadTextAsync(metadataText, MaxPageCharacters, token).ConfigureAwait(false); }
            catch (DatabaseBrowserException)
            {
                throw new DatabaseBrowserException("insert_not_allowed", "Метаданные таблицы превышают предел безопасного подтверждения. Вставка недоступна.");
            }
            canProtectSchema = reader.GetBoolean(2);
        }
        // Key validation is shared with updates, but CanUpdate and UPDATE privileges
        // are deliberately ignored when describing an INSERT.
        var editing = await ReadEditMetadataAsync(relationId, table, connection, transaction, token).ConfigureAwait(false);
        var reliableKey = editing.Keys.Count > 0 && editing.Keys.All(c => PostgresEditingSql.KeyType(c.TypeOid) is not null);
        var columns = new List<PostgresInsertColumn>();
        var usesSequence = false;
        await using (var command = Command(PostgresInsertionSql.Defaults, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relationId });
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var original = editing.Columns[columns.Count];
                if (reader.GetString(0) != original.Column.Name) throw InsertSchemaChanged();
                var identity = reader.GetBoolean(1);
                var generated = reader.GetBoolean(2);
                var domain = reader.GetBoolean(3);
                var hasDefault = reader.GetBoolean(4);
                var safeDefault = reader.GetBoolean(5);
                var hasSequence = reader.GetBoolean(6);
                var sequenceAllowed = reader.GetBoolean(7);
                var sequenceId = reader.IsDBNull(8) ? 0 : reader.GetFieldValue<uint>(8);
                fingerprint += $"|{original.Column.Ordinal}:{sequenceId}:{safeDefault}:{sequenceAllowed}";
                var mustDefault = identity || generated || original.SqlType is null || domain;
                var canSupply = !mustDefault;
                var reason = identity ? "Идентификатор заполняется сервером."
                    : generated ? "Вычисляемое поле заполняется сервером."
                    : domain ? "Нативный domain пока не поддерживает безопасную вставку."
                    : original.SqlType is null ? "Нативный тип пока допускает только безопасный серверный DEFAULT."
                    : null;
                var column = original.Column with { CanEdit = canSupply, ReadOnlyReason = reason };
                var description = generated ? "Вычисляется сервером"
                    : identity || hasSequence ? "Серверная последовательность"
                    : hasDefault ? "Серверное значение по умолчанию"
                    : safeDefault ? "NULL по умолчанию" : null;
                columns.Add(new PostgresInsertColumn(new InsertableDatabaseColumn(column, safeDefault, mustDefault, description,
                    reason ?? (hasDefault && !safeDefault ? "DEFAULT не разрешён: неподдерживаемое выражение или права последовательности." : null)),
                    original.TypeOid, original.SqlType, original.PrimaryKeyOrder));
                usesSequence |= hasSequence && safeDefault;
            }
        }
        if (columns.Count != editing.Columns.Count) throw InsertSchemaChanged();
        var satisfiable = columns.All(c => c.Capability.Column.CanEdit || c.Capability.CanUseDefault);
        var allowed = EditingEnabled && safe && reliableKey && satisfiable;
        var diagnostics = new List<DatabaseDiagnostic>
        {
            new("single_row_insert", "Добавляется одна строка. DEFAULT, NULL и явно заданное значение различаются; схема и права проверяются повторно."),
            new("insert_confirmation", "Подтверждение структуры действует только в текущем подключении до 15 минут. Открытие нового диалога добавления заменяет его.")
        };
        if (usesSequence)
            diagnostics.Add(new("sequence_gaps", "Серверные последовательности не откатываются: пропуски идентификаторов возможны после ошибки или отмены вставки. Откат строки не возвращает номер последовательности."));
        if (columns.Any(c => c.Capability.Column.CanEdit && c.Capability.Column.Kind == DatabaseEditValueKind.Decimal))
            diagnostics.Add(new("numeric_input_limit", $"Числовой ввод ограничен {DatabaseEditValueValidation.MaxDecimalCharacters} символами без округления."));
        if (!canProtectSchema)
            diagnostics.Add(new("insert_protective_lock_required", "Для защиты проверки структуры от одновременного DDL требуется право MAINTAIN (PostgreSQL 17+) либо одно из UPDATE, DELETE, TRUNCATE. Одних SELECT/INSERT недостаточно; SmartSQL не изменяет права автоматически."));
        if (!allowed)
            diagnostics.Add(new("insert_not_allowed", !EditingEnabled ? "Подключение открыто только для чтения."
                : !safe ? "Таблица или права SELECT/INSERT не допускают безопасную вставку."
                : !reliableKey ? "Нет подтверждённого первичного ключа поддерживаемого встроенного типа."
                : "Обязательное поле неподдерживаемого типа не имеет безопасного серверного DEFAULT."));
        return new InsertMetadata(relationId, fingerprint,
            new DatabaseInsertCapabilities(table, allowed, columns.Select(c => c.Capability).ToArray(), diagnostics), columns);
    }

    public async Task<DatabaseInsertResult> InsertRowAsync(DatabaseInsertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!EditingEnabled) throw InsertNotAllowed();
        try
        {
            ArgumentNullException.ThrowIfNull(request.Values);
            request = request with { Values = request.Values.ToArray() };
            request.Validate();
        }
        catch (ArgumentException) { throw InsertValueInvalid(); }
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var commitStarted = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var confirmation = _insertConfirmation;
            if (confirmation is null || confirmation.Handle != request.ConfirmationHandle
                || confirmation.Metadata.Capabilities.Table != request.Table
                || IsEditHandleExpired(confirmation.CreatedAt, DateTimeOffset.UtcNow)) throw InsertConfirmationExpired();
            var snapshot = confirmation.Metadata;
            var values = NormalizeInsertValues(snapshot, request.Values);
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            await using (var command = Command(PostgresBrowserSql.ConfigureReadOnlyTransaction.Replace("SET TRANSACTION READ ONLY;", "SET TRANSACTION READ WRITE;", StringComparison.Ordinal), connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = Command($"LOCK TABLE ONLY {PostgresEditingSql.Qualified(request.Table)} IN SHARE ROW EXCLUSIVE MODE", connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var relation = await ReadRelationAsync(request.Table, connection, transaction, cancellationToken).ConfigureAwait(false);
            var current = await ReadInsertMetadataAsync(relation.Id, relation.Table, connection, transaction, cancellationToken).ConfigureAwait(false);
            if (current.RelationId != snapshot.RelationId || current.Fingerprint != snapshot.Fingerprint) throw InsertSchemaChanged();
            if (!current.Capabilities.CanInsert) throw InsertNotAllowed();
            values = NormalizeInsertValues(current, values);
            if (IsEditHandleExpired(confirmation.CreatedAt, DateTimeOffset.UtcNow)) throw InsertConfirmationExpired();
            string?[] changed;
            await using (var command = Command(PostgresInsertionSql.Insert(request.Table, current.Columns, values), connection, transaction))
            {
                var supplied = values.Where(v => v.Mode != DatabaseInsertValueMode.ServerDefault).ToArray();
                foreach (var value in supplied)
                    command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)value.Value ?? DBNull.Value });
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw InsertNotAllowed();
                changed = await ReadEditRowAsync(reader, current.Columns.Count, cancellationToken).ConfigureAwait(false);
                var exact = reader.GetBoolean(current.Columns.Count);
                foreach (var value in supplied)
                {
                    var column = current.Columns.Single(c => c.Capability.Column.Name == value.ColumnName).Capability.Column;
                    if (column.Kind == DatabaseEditValueKind.Text && !string.Equals(changed[column.Ordinal], value.Value, StringComparison.Ordinal)) exact = false;
                }
                if (!exact) throw InsertPrecisionLoss();
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw InsertNotAllowed();
            }
            cancellationToken.ThrowIfCancellationRequested();
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            InvalidateInsertConfirmation();
            InvalidateEditSnapshots();
            return new DatabaseInsertResult(new DatabaseDisplayRow(changed), "Строка добавлена. Обновите данные перед следующей операцией.");
        }
        catch (Exception exception)
        {
            if (commitStarted)
            {
                InvalidateInsertConfirmation();
                InvalidateEditSnapshots();
                throw new DatabaseBrowserException("insert_commit_unknown", "Подтверждение добавления не получено. Результат неизвестен: обновите данные и проверьте строку. Не повторяйте вставку автоматически.");
            }
            if (exception is DatabaseBrowserException { Code: "object_unavailable" or "object_changed" or "edit_schema_changed" }) throw InsertSchemaChanged();
            if (exception is DatabaseBrowserException or ObjectDisposedException) throw;
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (exception is PostgresException postgres)
                throw postgres.SqlState switch
                {
                    "22003" or "22001" => InsertPrecisionLoss(),
                    "22P02" or "22007" or "22008" or "23502" or "23503" or "23505" or "23514" or "23P01" => InsertValueInvalid(),
                    "42501" or "25006" => InsertNotAllowed(),
                    "42P01" or "42703" or "42809" => InsertSchemaChanged(),
                    _ => Sanitize(postgres)
                };
            throw Sanitize(exception);
        }
        finally { _editGate.Release(); }
    }

    private static IReadOnlyList<DatabaseInsertValue> NormalizeInsertValues(InsertMetadata metadata, IReadOnlyList<DatabaseInsertValue> requested)
    {
        if (requested.Any(v => !metadata.Columns.Any(c => c.Capability.Column.Name == v.ColumnName))) throw InsertValueInvalid();
        var result = new List<DatabaseInsertValue>(metadata.Columns.Count);
        foreach (var column in metadata.Columns)
        {
            var value = requested.SingleOrDefault(v => v.ColumnName == column.Capability.Column.Name)
                ?? new DatabaseInsertValue(column.Capability.Column.Name, DatabaseInsertValueMode.ServerDefault);
            try { result.Add(DatabaseInsertValueValidation.Normalize(column.Capability, value)); }
            catch (ArgumentException) { throw InsertValueInvalid(); }
        }
        return result;
    }

    private static DatabaseBrowserException InsertNotAllowed() => new("insert_not_allowed", "Таблица, поле или права сервера не допускают безопасное добавление строки.");
    private static DatabaseBrowserException InsertSchemaChanged() => new("insert_schema_changed", "Структура или возможности таблицы изменились. Откройте добавление заново.");
    private static DatabaseBrowserException InsertConfirmationExpired() => new("insert_confirmation_expired", "Подтверждение структуры устарело. Откройте добавление заново.");
    private static DatabaseBrowserException InsertValueInvalid() => new("insert_value_invalid", "Значение, NULL или DEFAULT не соответствует полю либо ограничению таблицы. Строка не добавлена.");
    private static DatabaseBrowserException InsertPrecisionLoss() => new("insert_precision_loss", "Сервер изменил точность или содержимое значения либо тип переполнен. Вставка отменена без потери данных.");
}
