using System.Globalization;
using System.Numerics;
using System.Text;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class NativeSqliteDatabaseBrowser
{
    public async Task<DatabaseUpdateResult> UpdateCellAsync(DatabaseCellUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!EditingEnabled) throw new DatabaseBrowserException("edit_not_allowed", "Подключение открыто только для чтения.");
        try { update.Validate(); Validate(update.Table); }
        catch (ArgumentException) { throw new DatabaseBrowserException("edit_value_invalid", "Некорректный запрос изменения ячейки."); }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var admitted = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_editSnapshots.TryGetValue(update.EditHandle, out var snapshot) || snapshot.Table != update.Table ||
                DateTimeOffset.UtcNow - snapshot.CreatedAt >= NativeEditLifetime)
                throw new DatabaseBrowserException("edit_handle_expired", "Разрешение строки устарело. Обновите страницу перед изменением.");
            _editSnapshots.Remove(update.EditHandle);
            admitted = true;
            var column = snapshot.Metadata.Capabilities.Columns.SingleOrDefault(column => column.Name == update.ColumnName);
            if (column?.CanEdit != true)
                throw new DatabaseBrowserException("edit_not_allowed", "Это поле недоступно для безопасного редактирования.");
            object value;
            try
            {
                var normalized = DatabaseEditValueValidation.Normalize(column, update.Value);
                value = normalized is null ? DBNull.Value : FilterValue(QueryKind(column.NativeType), normalized);
                var declaration = column.NativeType.Trim().ToUpperInvariant();
                if (value is long integer && QueryKind(column.NativeType) == DatabaseFilterValueKind.Number &&
                    (declaration.StartsWith("REAL", StringComparison.Ordinal) || declaration.StartsWith("FLOAT", StringComparison.Ordinal) || declaration.StartsWith("DOUBLE", StringComparison.Ordinal)) &&
                    !AssignedExactly(new("real", (double)integer), integer, StrictUtf8))
                    throw new ArgumentException("Целое значение теряет точность в REAL affinity.");
            }
            catch (Exception exception) when (exception is ArgumentException or DatabaseBrowserException)
            {
                throw new DatabaseBrowserException("edit_value_invalid", "Значение не соответствует нативному типу, NULL или требованию точности. Изменение отменено.");
            }
            return await Task.Run(() => UpdateCellCore(update, snapshot, column, value, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (admitted) InvalidateEditSnapshots();
            _gate.Release();
        }
    }

    private DatabaseUpdateResult UpdateCellCore(DatabaseCellUpdate update, NativeEditSnapshot snapshot,
        EditableDatabaseColumn column, object value, CancellationToken cancellationToken)
    {
        var commitStarted = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3
        }.ToString());
        try
        {
            token.ThrowIfCancellationRequested();
            RejectReparseAncestors(_path);
            connection.Open();
            connection.EnableExtensions(false);
            using var registration = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
            using (var settings = connection.CreateCommand())
            {
                settings.CommandTimeout = 3;
                settings.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=0";
                settings.ExecuteNonQuery();
                settings.CommandText = "PRAGMA foreign_keys";
                if (Convert.ToInt64(settings.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                    throw new DatabaseBrowserException("edit_not_allowed", "Не подтверждена защитная проверка внешних ключей SQLite.");
                settings.CommandText = "PRAGMA journal_mode";
                if ((settings.ExecuteScalar() as string)?.ToLowerInvariant() is not ("delete" or "truncate" or "persist" or "wal"))
                    throw new DatabaseBrowserException("edit_not_allowed", "Режим журнала SQLite не гарантирует откат. Файл оставлен без изменения режима.");
            }
            using var transaction = connection.BeginTransaction(deferred: false); // BEGIN IMMEDIATE guards writers and schema DDL.
            var encoding = ReadEditEncoding(connection, transaction);
            var context = new Context(connection, transaction, token, encoding);
            NativeRelation relation;
            try { relation = Relation(context, update.Table); }
            catch (DatabaseBrowserException exception) when (exception.Code is "object_unavailable" or "object_changed" or "unsupported_object")
            { throw new DatabaseBrowserException("edit_schema_changed", "Объект изменился. Обновите страницу перед изменением."); }
            var metadata = ReadEditMetadata(context, relation, Columns(context, relation));
            if (metadata.Fingerprint != snapshot.Metadata.Fingerprint)
                throw new DatabaseBrowserException("edit_schema_changed", "Структура базы данных изменилась. Обновите страницу перед изменением.");
            if (!metadata.Capabilities.CanUpdate || !metadata.Capabilities.Columns.Any(candidate => candidate.Name == column.Name && candidate.CanEdit))
                throw new DatabaseBrowserException("edit_not_allowed", "Текущая структура или значения больше не допускают безопасное изменение поля.");
            if (DateTimeOffset.UtcNow - snapshot.CreatedAt >= NativeEditLifetime)
                throw new DatabaseBrowserException("edit_handle_expired", "Разрешение строки устарело. Обновите страницу.");
            var keys = metadata.Columns.Where(candidate => candidate.PrimaryKeyOrder is not null).OrderBy(candidate => candidate.PrimaryKeyOrder).ToArray();
            var original = ReadOneEditRow(context, update.Table, metadata.Columns, keys, snapshot.Values);
            if (!SameRawRow(original, snapshot.Values)) throw NativeEditConflict();

            token.ThrowIfCancellationRequested();
            var backupPath = CreateNativeEditBackup(context, metadata.Fingerprint);
            token.ThrowIfCancellationRequested();
            NativeRawValue[] changed;
            using (var command = context.Command($"UPDATE OR ABORT main.{NativeSqliteBrowserSql.Quote(update.Table.Name)} SET {NativeSqliteBrowserSql.Quote(column.Name)}=$value WHERE {EditKeyPredicate(keys)} RETURNING {EditProjection(metadata.Columns)}"))
            {
                AddEditKeys(command, keys, snapshot.Values, encoding);
                command.Parameters.AddWithValue("$value", value);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) throw NativeEditConflict();
                changed = ReadRawRow(reader, metadata.Columns.Count);
                for (var index = 0; index < changed.Length; index++)
                    if (index != column.Ordinal && !SameRaw(original[index], changed[index]))
                        throw new DatabaseBrowserException("edit_precision_loss", "SQLite изменил другое поле строки. Изменение отменено.");
                if (!AssignedExactly(changed[column.Ordinal], value, encoding))
                    throw new DatabaseBrowserException("edit_precision_loss", "SQLite изменил класс или точность значения при присваивании. Изменение отменено без потери данных.");
                if (reader.Read()) throw NativeEditConflict();
            }
            var display = changed.Select(cell => EditDisplay(cell, encoding)).ToArray();
            if (display.Any(text => text?.Length > MaxCell) || display.Sum(text => (long)(text?.Length ?? 0)) > MaxPage)
                throw new DatabaseBrowserException("edit_value_invalid", "Изменённая строка превышает ограничение безопасного представления. Изменение отменено.");
            var result = new DatabaseUpdateResult(new DatabaseDisplayRow(display),
                $"Резервная копия до изменения: {backupPath}.");
            token.ThrowIfCancellationRequested();
            // Dispose waits for an already-running interrupt callback before the final cancellation check.
            registration.Dispose();
            token.ThrowIfCancellationRequested();
            commitStarted = true;
            transaction.Commit(); // Cancellation after this boundary must not claim rollback.
            return result;
        }
        catch (Exception exception)
        {
            if (commitStarted)
                throw new DatabaseBrowserException("edit_commit_unknown", "Подтверждение записи не получено. Результат неизвестен: перечитайте данные и проверьте ячейку. Автоматического повтора нет.");
            if (exception is DatabaseBrowserException or ObjectDisposedException) throw;
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);
            if (timeout.IsCancellationRequested)
                throw new DatabaseBrowserException("operation_timed_out", "Проверка, копирование или изменение SQLite превысили 15 секунд. Изменение отменено.");
            if (exception is SqliteException sqlite)
                throw sqlite.SqliteErrorCode switch
                {
                    5 or 6 => new DatabaseBrowserException("edit_conflict", "Файл занят другим подключением. Изменение отменено; обновите страницу перед повторной проверкой."),
                    19 => new DatabaseBrowserException("edit_value_invalid", "Значение нарушает ограничение SQLite. Изменение отменено."),
                    _ => new DatabaseBrowserException("edit_not_allowed", "SQLite не смог безопасно изменить файл. Проверьте доступ, структуру и ограничения.")
                };
            throw new DatabaseBrowserException("edit_not_allowed", "Не удалось безопасно изменить SQLite-файл. Изменение отменено.");
        }
    }

    private static Encoding ReadEditEncoding(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = new SqliteCommand("PRAGMA main.encoding", connection, transaction) { CommandTimeout = 3 };
        return (string?)command.ExecuteScalar() switch
        {
            "UTF-8" => StrictUtf8,
            "UTF-16le" => new UnicodeEncoding(false, false, true),
            "UTF-16be" => new UnicodeEncoding(true, false, true),
            _ => throw new DatabaseBrowserException("edit_not_allowed", "Кодировка SQLite не подтверждена для безопасной записи.")
        };
    }

    private static string EditProjection(IReadOnlyList<DatabaseColumn> columns) => string.Join(", ", columns.Select(column =>
    {
        var name = NativeSqliteBrowserSql.Quote(column.Name);
        return $"typeof({name}), length(CAST({name} AS BLOB)), CASE WHEN typeof({name})='text' THEN CAST({name} AS BLOB) ELSE {name} END";
    }));

    private static string EditKeyPredicate(IReadOnlyList<DatabaseColumn> keys) => string.Join(" AND ", keys.Select((key, index) =>
        $"{NativeSqliteBrowserSql.Quote(key.Name)} COLLATE BINARY IS $key{index}"));

    private static void AddEditKeys(SqliteCommand command, IReadOnlyList<DatabaseColumn> keys,
        NativeRawValue[] values, Encoding encoding)
    {
        for (var index = 0; index < keys.Count; index++)
        {
            var value = values[keys[index].Ordinal];
            object key = value.Storage switch
            {
                "integer" => (long)value.Value!,
                "text" => Decode((byte[])value.Value!, encoding),
                _ => throw new DatabaseBrowserException("edit_not_allowed", "Класс хранения ключа не подтверждён.")
            };
            command.Parameters.AddWithValue("$key" + index, key);
        }
    }

    private static NativeRawValue[] ReadOneEditRow(Context context, DatabaseTableRef table,
        IReadOnlyList<DatabaseColumn> columns, IReadOnlyList<DatabaseColumn> keys, NativeRawValue[] original)
    {
        using var command = context.Command($"SELECT {EditProjection(columns)} FROM main.{NativeSqliteBrowserSql.Quote(table.Name)} WHERE {EditKeyPredicate(keys)} LIMIT 2");
        AddEditKeys(command, keys, original, context.TextEncoding);
        using var reader = command.ExecuteReader();
        context.Token.ThrowIfCancellationRequested();
        if (!reader.Read()) throw NativeEditConflict();
        var result = ReadRawRow(reader, columns.Count);
        if (reader.Read()) throw NativeEditConflict();
        return result;
    }

    private static bool SameRawRow(NativeRawValue[] first, NativeRawValue[] second) => first.Length == second.Length &&
        first.Zip(second).All(pair => SameRaw(pair.First, pair.Second));

    private static bool AssignedExactly(NativeRawValue actual, object expected, Encoding encoding) => expected switch
    {
        DBNull => actual.Storage == "null",
        string text => actual.Storage == "text" && ((byte[])actual.Value!).AsSpan().SequenceEqual(encoding.GetBytes(text)),
        long integer => actual.Storage == "integer" && (long)actual.Value! == integer ||
            actual.Storage == "real" && double.IsFinite((double)actual.Value!) &&
            Math.Truncate((double)actual.Value!) == (double)actual.Value! && new BigInteger((double)actual.Value!) == integer,
        double number => actual.Storage == "real" && BitConverter.DoubleToInt64Bits((double)actual.Value!) == BitConverter.DoubleToInt64Bits(number) ||
            actual.Storage == "integer" && Math.Truncate(number) == number && new BigInteger(number) == (long)actual.Value!,
        _ => false
    };

    private static string? EditDisplay(NativeRawValue value, Encoding encoding) => value.Storage switch
    {
        "null" => null,
        "integer" => ((long)value.Value!).ToString(CultureInfo.InvariantCulture),
        "real" => ((double)value.Value!).ToString("R", CultureInfo.InvariantCulture),
        "text" => Decode((byte[])value.Value!, encoding),
        "blob" => "0x" + Convert.ToHexString((byte[])value.Value!),
        _ => throw new DatabaseBrowserException("edit_not_allowed", "Неизвестный класс хранения SQLite.")
    };

    private static DatabaseBrowserException NativeEditConflict() => new("edit_conflict",
        "Строка изменилась или удалена другим подключением. Изменение отменено; перечитайте страницу. Автоматического повтора нет.");
}
