using System.Data;
using Npgsql;
using NpgsqlTypes;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class PostgresDatabaseBrowser
{
    private readonly Dictionary<Guid, DeleteSnapshot> _deleteSnapshots = [];
    private sealed record DeleteMetadata(uint RelationId, string Fingerprint, EditMetadata Editing,
        DatabaseDeleteCapabilities Capabilities);
    private sealed record DeleteSnapshot(DatabaseTableRef Table, DeleteMetadata Metadata,
        string?[] Values, string Version, DateTimeOffset CreatedAt);

    public Task<DatabaseDeleteCapabilities> GetDeleteCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        ValidateTable(table);
        return ReadAsync(async (connection, transaction, token) =>
        {
            var relation = await ReadRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
            return (await ReadDeleteMetadataAsync(relation.Id, relation.Table, connection, transaction, token).ConfigureAwait(false)).Capabilities;
        }, cancellationToken);
    }

    private async Task<DeleteMetadata> ReadDeleteMetadataAsync(uint relationId, DatabaseTableRef table,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token, EditMetadata? editing = null)
    {
        bool safeTable;
        string fingerprint;
        await using (var command = Command(PostgresDeletionSql.TableSafety, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relationId });
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw DeleteSchemaChanged();
            safeTable = reader.GetBoolean(0) && table.Kind == DatabaseObjectKind.Table;
            using var text = await reader.GetTextReaderAsync(1, token).ConfigureAwait(false);
            fingerprint = await ReadTextAsync(text, MaxPageCharacters, token).ConfigureAwait(false);
        }
        bool noIncomingForeignKeys;
        await using (var command = Command(PostgresDeletionSql.IncomingForeignKeys, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relationId });
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw DeleteSchemaChanged();
            noIncomingForeignKeys = reader.GetBoolean(0);
            using var text = await reader.GetTextReaderAsync(1, token).ConfigureAwait(false);
            fingerprint += "|" + await ReadTextAsync(text, MaxPageCharacters, token).ConfigureAwait(false);
        }
        editing ??= await ReadEditMetadataAsync(relationId, table, connection, transaction, token).ConfigureAwait(false);
        var reliableKey = editing.Keys.Count > 0 && editing.Keys.All(c => PostgresEditingSql.KeyType(c.TypeOid) is not null);
        var allowed = EditingEnabled && safeTable && reliableKey && noIncomingForeignKeys;
        var diagnostics = new List<DatabaseDiagnostic>
        {
            new("single_row_delete", "Удаляется одна выбранная строка по подтверждённому полному первичному ключу. Проверяются структура, права, исходные значения и версия строки."),
            new("ephemeral_delete_handles", "Разрешение удаления хранится только для текущей страницы до 15 минут. Любая попытка удаления использует его один раз; после ошибки обновите страницу.")
        };
        if (!noIncomingForeignKeys)
            diagnostics.Add(new("delete_incoming_foreign_keys", "Другие таблицы ссылаются на эту таблицу. Первый этап запрещает удаление при любом входящем внешнем ключе, включая RESTRICT/NO ACTION, CASCADE, SET NULL и SET DEFAULT. Связанные записи не изменяются."));
        if (!allowed)
            diagnostics.Add(new("delete_not_allowed", !EditingEnabled ? "Подключение открыто только для чтения."
                : !noIncomingForeignKeys ? "Удаление недоступно из-за входящих внешних ключей."
                : !safeTable ? "Нужны права SELECT/DELETE и обычная безопасная таблица без наследования, секционирования, RLS, пользовательских триггеров, DELETE-правил и небезопасных выражений/вывода типов."
                : "Нет подтверждённого первичного ключа поддерживаемого встроенного типа."));
        return new DeleteMetadata(relationId, fingerprint, editing,
            new DatabaseDeleteCapabilities(table, allowed, diagnostics));
    }

    public async Task<DatabaseDeleteResult> DeleteRowAsync(DatabaseRowDelete request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!EditingEnabled) throw DeleteNotAllowed();
        try { request.Validate(); }
        catch (ArgumentException) { throw new DatabaseBrowserException("delete_not_allowed", "Некорректный запрос удаления. Выберите строку текущей страницы."); }
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var commitStarted = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            DeleteSnapshot snapshot;
            lock (_snapshotSync)
            {
                if (!_deleteSnapshots.TryGetValue(request.RowHandle, out snapshot!) || snapshot.Table != request.Table
                    || IsEditHandleExpired(snapshot.CreatedAt, DateTimeOffset.UtcNow)) throw DeleteHandleExpired();
                // A submitted deletion capability is single-use, including rollback or
                // cancellation after admission. Do not allow reusing stale confirmation.
                _deleteSnapshots.Remove(request.RowHandle);
            }
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            await using (var command = Command(PostgresBrowserSql.ConfigureReadOnlyTransaction.Replace("SET TRANSACTION READ ONLY;", "SET TRANSACTION READ WRITE;", StringComparison.Ordinal), connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = Command($"LOCK TABLE ONLY {PostgresEditingSql.Qualified(request.Table)} IN SHARE ROW EXCLUSIVE MODE", connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var relation = await ReadRelationAsync(request.Table, connection, transaction, cancellationToken).ConfigureAwait(false);
            var current = await ReadDeleteMetadataAsync(relation.Id, relation.Table, connection, transaction, cancellationToken).ConfigureAwait(false);
            if (current.RelationId != snapshot.Metadata.RelationId || current.Fingerprint != snapshot.Metadata.Fingerprint) throw DeleteSchemaChanged();
            if (!current.Capabilities.CanDelete) throw DeleteNotAllowed();
            if (IsEditHandleExpired(snapshot.CreatedAt, DateTimeOffset.UtcNow)) throw DeleteHandleExpired();
            var keys = current.Editing.Keys.Select(c => c with { SqlType = PostgresEditingSql.KeyType(c.TypeOid) }).ToArray();
            await using (var command = Command(PostgresDeletionSql.ReadOriginal(request.Table, current.Editing.Columns, keys), connection, transaction))
            {
                AddKeys(command, keys, snapshot.Values);
                await VerifyDeletedOriginalAsync(command, snapshot, cancellationToken).ConfigureAwait(false);
            }
            await using (var command = Command(PostgresDeletionSql.Delete(request.Table, current.Editing.Columns, keys), connection, transaction))
            {
                AddKeys(command, keys, snapshot.Values);
                await VerifyDeletedOriginalAsync(command, snapshot, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            InvalidateEditSnapshots();
            return new DatabaseDeleteResult("Строка удалена. Обновите страницу перед следующим изменением.");
        }
        catch (Exception exception)
        {
            if (commitStarted)
            {
                InvalidateEditSnapshots();
                throw new DatabaseBrowserException("delete_commit_unknown", "Подтверждение удаления не получено. Результат неизвестен: обновите данные и проверьте строку. Не повторяйте удаление автоматически.");
            }
            if (exception is DatabaseBrowserException { Code: "object_unavailable" or "object_changed" or "edit_schema_changed" }) throw DeleteSchemaChanged();
            if (exception is DatabaseBrowserException or ObjectDisposedException) throw;
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (exception is PostgresException postgres)
                throw postgres.SqlState switch
                {
                    "23503" => new DatabaseBrowserException("delete_referenced", "Сервер запретил удаление из-за связанных строк. Удаление отменено; обновите структуру и данные."),
                    "42501" or "25006" => DeleteNotAllowed(),
                    "42P01" or "42703" or "42809" => DeleteSchemaChanged(),
                    "40001" or "40P01" or "55P03" or "57014" => DeleteConflict(),
                    _ => Sanitize(postgres)
                };
            throw Sanitize(exception);
        }
        finally { _editGate.Release(); }
    }

    private static async Task VerifyDeletedOriginalAsync(NpgsqlCommand command, DeleteSnapshot snapshot, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw DeleteConflict();
        var original = await ReadEditRowAsync(reader, snapshot.Values.Length, token).ConfigureAwait(false);
        var version = reader.GetString(snapshot.Values.Length);
        if (version != snapshot.Version || !original.SequenceEqual(snapshot.Values, StringComparer.Ordinal)
            || await reader.ReadAsync(token).ConfigureAwait(false)) throw DeleteConflict();
    }

    private static DatabaseBrowserException DeleteNotAllowed() => new("delete_not_allowed", "Объект, права или связи таблицы не допускают безопасное удаление одной строки.");
    private static DatabaseBrowserException DeleteSchemaChanged() => new("delete_schema_changed", "Структура или связи таблицы изменились. Обновите страницу перед удалением.");
    private static DatabaseBrowserException DeleteHandleExpired() => new("delete_handle_expired", "Разрешение удаления устарело или уже использовано. Обновите страницу и заново выберите строку.");
    private static DatabaseBrowserException DeleteConflict() => new("delete_conflict", "Строка изменилась, удалена или заблокирована другим подключением. Удаление отменено; обновите страницу. Автоматического повтора нет.");
}
