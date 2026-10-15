using Npgsql;
using NpgsqlTypes;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class PostgresDatabaseBrowser
{
    public async Task<DatabaseQueryCapabilities> GetQueryCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        ValidateTable(table);
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InvalidateEditSnapshots();
            return await ReadAsync(async (connection, transaction, token) =>
            {
                await LockQueryRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
                var relation = await ReadRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
                var columns = await ReadQueryColumnsAsync(relation.Id, connection, transaction, token).ConfigureAwait(false);
                return PostgresQuerySql.Describe(relation.Table, columns);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _editGate.Release(); }
    }

    // Parsing this zero-row read takes ACCESS SHARE before inspecting types; DDL cannot
    // replace or alter this relation between capability validation and the actual page.
    private static async Task LockQueryRelationAsync(DatabaseTableRef table, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = Command($"SELECT 1 FROM {PostgresBrowserSql.QuoteIdentifier(table.Schema)}.{PostgresBrowserSql.QuoteIdentifier(table.Name)} LIMIT 0", connection, transaction);
        try { await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            throw new DatabaseBrowserException("object_unavailable", "Объект не найден или нет прав на чтение. Обновите список таблиц.");
        }
    }

    private static async Task<IReadOnlyList<PostgresQuerySql.Column>> ReadQueryColumnsAsync(uint relation,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = Command(PostgresQuerySql.Columns, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relation });
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var columns = new List<PostgresQuerySql.Column>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            columns.Add(PostgresQuerySql.Confirm(reader.GetString(0), reader.GetFieldValue<uint>(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return columns;
    }
}
