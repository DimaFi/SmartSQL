using System.Data;
using Npgsql;
using NpgsqlTypes;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class PostgresDatabaseBrowser
{
    private readonly SemaphoreSlim _editGate = new(1, 1);
    private readonly object _snapshotSync = new();
    private readonly Dictionary<Guid, EditSnapshot> _editSnapshots = [];
    public bool EditingEnabled { get; private init; }
    internal static readonly TimeSpan EditHandleLifetime = TimeSpan.FromMinutes(15);

    private sealed record EditMetadata(uint RelationId, DatabaseEditCapabilities Capabilities,
        string Fingerprint, IReadOnlyList<PostgresEditColumn> Columns)
    {
        public IReadOnlyList<PostgresEditColumn> Keys => Columns.Where(c => c.PrimaryKeyOrder is not null).OrderBy(c => c.PrimaryKeyOrder).ToArray();
    }

    private sealed record EditSnapshot(DatabaseTableRef Table, EditMetadata Metadata,
        string?[] Values, string Version, DateTimeOffset CreatedAt);

    internal static bool IsEditHandleExpired(DateTimeOffset createdAt, DateTimeOffset now) =>
        now - createdAt >= EditHandleLifetime;

    private void InvalidateEditSnapshots()
    {
        lock (_snapshotSync)
            _editSnapshots.Clear();
    }

    public Task<DatabaseEditCapabilities> GetEditCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        ValidateTable(table);
        return ReadAsync(async (connection, transaction, token) =>
        {
            var relation = await ReadRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
            return (await ReadEditMetadataAsync(relation.Id, relation.Table, connection, transaction, token).ConfigureAwait(false)).Capabilities;
        }, cancellationToken);
    }

    private async Task<EditMetadata> ReadEditMetadataAsync(uint relationId, DatabaseTableRef table,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        bool safeTable;
        string fingerprint;
        await using (var command = Command(PostgresEditingSql.TableSafety, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relationId });
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new DatabaseBrowserException("edit_schema_changed", "Объект изменился. Обновите страницу перед редактированием.");
            safeTable = reader.GetBoolean(0) && table.Kind == DatabaseObjectKind.Table;
            fingerprint = reader.GetString(1);
        }
        var columns = new List<PostgresEditColumn>();
        await using (var command = Command(PostgresEditingSql.Columns, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relationId });
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                var nativeType = reader.GetString(1);
                var nullable = reader.GetBoolean(2);
                var oid = reader.GetFieldValue<uint>(3);
                var typmod = reader.GetInt32(4);
                var generated = reader.GetBoolean(5);
                var unique = reader.GetBoolean(6);
                int? keyOrder = reader.IsDBNull(7) ? null : reader.GetInt32(7);
                var (kind, sqlType) = PostgresEditingSql.ValueType(oid);
                var reason = keyOrder is not null ? "Первичный ключ защищён от изменения."
                    : generated ? "Автоматическое или вычисляемое поле доступно только для чтения."
                    : unique ? "Поле уникального индекса защищено от изменения."
                    : sqlType is null ? "Этот нативный тип пока не поддерживает безопасное редактирование."
                    : null;
                columns.Add(new PostgresEditColumn(new EditableDatabaseColumn(name, columns.Count, nativeType, kind,
                    reason is null, nullable, oid == 1043 && typmod >= 4 ? typmod - 4 : null, reason), oid, sqlType, keyOrder));
            }
        }
        var keys = columns.Where(c => c.PrimaryKeyOrder is not null).ToArray();
        var reliableKey = keys.Length > 0 && keys.All(c => PostgresEditingSql.KeyType(c.TypeOid) is not null);
        var canUpdate = EditingEnabled && safeTable && reliableKey && columns.Any(c => c.Column.CanEdit);
        var tableReason = !EditingEnabled ? "Подключение открыто только для чтения. Для изменений откройте явный режим редактирования."
            : !safeTable ? "Редактирование доступно только обычной таблице без наследования, секционирования, RLS, пользовательских триггеров, правил UPDATE и небезопасных функций ограничений, вычисляемых полей, индексов или вывода типов, с правами SELECT и UPDATE."
            : !reliableKey ? "Нет подтверждённого первичного ключа из поддерживаемых встроенных типов."
            : !canUpdate ? "Нет полей, которые можно безопасно изменить на текущем этапе."
            : null;
        if (!canUpdate)
            columns = columns.Select(c => c with { Column = c.Column with { CanEdit = false, ReadOnlyReason = c.Column.ReadOnlyReason ?? tableReason } }).ToList();
        var diagnostics = new List<DatabaseDiagnostic>
        {
            new("single_cell_edit", "Меняется только одна ячейка по подтверждённому первичному ключу. Строка и структура повторно проверяются; при конфликте обновите страницу. Ключи и неподдерживаемые типы защищены."),
            new("ephemeral_edit_handles", "Разрешение строки действует только для текущей страницы до 15 минут. Загрузка другой страницы или успешное изменение требует нового чтения.")
        };
        if (tableReason is not null)
            diagnostics.Add(new("edit_not_allowed", tableReason));
        if (columns.Any(c => c.Column.CanEdit && c.Column.Kind == DatabaseEditValueKind.Decimal))
            diagnostics.Add(new("numeric_input_limit", $"Числовой ввод ограничен {DatabaseEditValueValidation.MaxDecimalCharacters} символами записи. Более длинные нативные числа доступны для просмотра; ввод не обрезается."));
        if (columns.Any(c => c.Column.CanEdit && c.Column.Kind == DatabaseEditValueKind.Date))
            diagnostics.Add(new("date_input_limit", "Дата вводится в формате ГГГГ-ММ-ДД для годов 0001–9999. Нативные бесконечные даты и даты вне этого диапазона доступны для просмотра."));
        return new EditMetadata(relationId, new DatabaseEditCapabilities(table, canUpdate, columns.Select(c => c.Column).ToArray(), diagnostics), fingerprint, columns);
    }

    public async Task<DatabaseUpdateResult> UpdateCellAsync(DatabaseCellUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!EditingEnabled)
            throw new DatabaseBrowserException("edit_not_allowed", "Подключение открыто только для чтения.");
        try { update.Validate(); }
        catch (ArgumentException) { throw new DatabaseBrowserException("edit_value_invalid", "Некорректный запрос изменения ячейки."); }
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var commitStarted = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            EditSnapshot snapshot;
            lock (_snapshotSync)
            {
                if (!_editSnapshots.TryGetValue(update.EditHandle, out snapshot!)
                    || snapshot.Table != update.Table || IsEditHandleExpired(snapshot.CreatedAt, DateTimeOffset.UtcNow))
                    throw new DatabaseBrowserException("edit_handle_expired", "Разрешение строки устарело. Обновите страницу перед изменением.");
            }
            var target = snapshot.Metadata.Columns.SingleOrDefault(c => string.Equals(c.Column.Name, update.ColumnName, StringComparison.Ordinal));
            if (target is null || !target.Column.CanEdit)
                throw new DatabaseBrowserException("edit_not_allowed", "Это поле недоступно для безопасного редактирования.");
            string? value;
            try { value = DatabaseEditValueValidation.Normalize(target.Column, update.Value); }
            catch (ArgumentException) { throw new DatabaseBrowserException("edit_value_invalid", "Значение не соответствует типу, допустимой длине или правилу NULL выбранного поля."); }
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            await using (var command = Command(PostgresBrowserSql.ConfigureReadOnlyTransaction.Replace("SET TRANSACTION READ ONLY;", "SET TRANSACTION READ WRITE;", StringComparison.Ordinal), connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            // Also exclude SHARE UPDATE EXCLUSIVE (including concurrent index DDL)
            // throughout the metadata check and write, while ordinary reads continue.
            await using (var command = Command($"LOCK TABLE ONLY {PostgresEditingSql.Qualified(update.Table)} IN SHARE ROW EXCLUSIVE MODE", connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var relation = await ReadRelationAsync(update.Table, connection, transaction, cancellationToken).ConfigureAwait(false);
            var current = await ReadEditMetadataAsync(relation.Id, relation.Table, connection, transaction, cancellationToken).ConfigureAwait(false);
            if (current.RelationId != snapshot.Metadata.RelationId || current.Fingerprint != snapshot.Metadata.Fingerprint)
                throw new DatabaseBrowserException("edit_schema_changed", "Структура таблицы изменилась. Обновите страницу перед изменением.");
            if (!current.Capabilities.CanUpdate || !current.Columns.Any(c => c.Column.Name == target.Column.Name && c.Column.CanEdit))
                throw new DatabaseBrowserException("edit_not_allowed", "Сервер больше не допускает безопасное изменение этого поля.");
            var keys = current.Keys.Select(c => c with { SqlType = PostgresEditingSql.KeyType(c.TypeOid) }).ToArray();
            await using (var command = Command(PostgresEditingSql.LockRow(update.Table, current.Columns, keys), connection, transaction))
            {
                AddKeys(command, keys, snapshot.Values);
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw Conflict();
                var original = await ReadEditRowAsync(reader, current.Columns.Count, cancellationToken).ConfigureAwait(false);
                var version = reader.GetString(current.Columns.Count);
                if (version != snapshot.Version || !original.SequenceEqual(snapshot.Values, StringComparer.Ordinal)
                    || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw Conflict();
            }
            lock (_snapshotSync)
                if (!_editSnapshots.ContainsKey(update.EditHandle)
                    || IsEditHandleExpired(snapshot.CreatedAt, DateTimeOffset.UtcNow))
                    throw new DatabaseBrowserException("edit_handle_expired", "Страница была обновлена. Повторно выберите строку.");
            string?[] changed;
            await using (var command = Command(PostgresEditingSql.Update(update.Table, current.Columns, keys, target), connection, transaction))
            {
                AddKeys(command, keys, snapshot.Values);
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value });
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw Conflict();
                changed = await ReadEditRowAsync(reader, current.Columns.Count, cancellationToken).ConfigureAwait(false);
                var equal = reader.GetBoolean(current.Columns.Count);
                if (!equal || target.Column.Kind == DatabaseEditValueKind.Text && !string.Equals(changed[target.Column.Ordinal], value, StringComparison.Ordinal))
                    throw new DatabaseBrowserException("edit_precision_loss", "Сервер изменил точность или содержимое значения. Изменение отменено; данные сохранены без потери.");
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw Conflict();
            }
            cancellationToken.ThrowIfCancellationRequested();
            commitStarted = true;
            // Once COMMIT is sent, cancellation cannot honestly imply rollback. Never retry an unknown outcome.
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            InvalidateEditSnapshots();
            return new DatabaseUpdateResult(new DatabaseDisplayRow(changed), "Ячейка сохранена. Обновите страницу перед следующим изменением.");
        }
        catch (Exception exception)
        {
            if (commitStarted)
            {
                InvalidateEditSnapshots();
                throw new DatabaseBrowserException("edit_commit_unknown", "Подтверждение записи не получено. Результат неизвестен: обновите данные и проверьте ячейку. Не повторяйте изменение автоматически.");
            }
            if (exception is DatabaseBrowserException { Code: "object_unavailable" or "object_changed" })
                throw new DatabaseBrowserException("edit_schema_changed", "Объект или структура изменились. Обновите страницу.");
            if (exception is DatabaseBrowserException or ObjectDisposedException)
                throw;
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (exception is PostgresException postgres)
                throw postgres.SqlState switch
                {
                    "22003" or "22001" => new DatabaseBrowserException("edit_precision_loss", "Значение не помещается в нативный тип поля. Изменение отменено без обрезки или округления."),
                    "22P02" or "22007" or "22008" or "23502" => new DatabaseBrowserException("edit_value_invalid", "Сервер отклонил тип значения или NULL. Изменение отменено."),
                    "23503" or "23505" or "23514" or "23P01" => new DatabaseBrowserException("edit_value_invalid", "Значение нарушает ограничение таблицы. Изменение отменено."),
                    "42501" or "25006" => new DatabaseBrowserException("edit_not_allowed", "Сервер не разрешает изменение этой ячейки."),
                    "42P01" or "42703" or "42809" => new DatabaseBrowserException("edit_schema_changed", "Объект или структура изменились. Обновите страницу."),
                    "40001" or "40P01" => Conflict(),
                    "55P03" or "57014" => new DatabaseBrowserException("edit_conflict", "Строка занята или сервер превысил время ожидания. Изменение отменено. Обновите страницу перед повторной проверкой."),
                    _ => Sanitize(postgres)
                };
            throw Sanitize(exception);
        }
        finally { _editGate.Release(); }
    }

    private static DatabaseBrowserException Conflict() => new("edit_conflict", "Строка изменилась или была удалена другим подключением. Изменение отменено. Обновите страницу; автоматического повтора нет.");

    private static void AddKeys(NpgsqlCommand command, IReadOnlyList<PostgresEditColumn> keys, IReadOnlyList<string?> values)
    {
        foreach (var key in keys)
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)values[key.Column.Ordinal] ?? DBNull.Value });
    }

    private static async Task<string?[]> ReadEditRowAsync(NpgsqlDataReader reader, int columns, CancellationToken token)
    {
        var values = new string?[columns];
        var characters = 0;
        for (var index = 0; index < columns; index++)
        {
            if (await reader.IsDBNullAsync(index, token).ConfigureAwait(false)) continue;
            using var text = await reader.GetTextReaderAsync(index, token).ConfigureAwait(false);
            values[index] = await ReadTextAsync(text, MaxCellCharacters, token).ConfigureAwait(false);
            characters += values[index]!.Length;
            if (characters > MaxPageCharacters)
                throw new DatabaseBrowserException("page_too_large", "Строка слишком велика для безопасного изменения. Данные не обрезаны.");
        }
        return values;
    }
}
