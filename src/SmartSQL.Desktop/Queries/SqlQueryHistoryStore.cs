using System.Text;
using System.Text.Json;
using SmartSQL.Application;

namespace SmartSQL.Desktop.Queries;

public sealed record SqlQueryHistoryEntry(DateTimeOffset ExecutedUtc, string ConnectionName, string Provider,
    string Sql, int RowCount, bool Truncated);

/// <summary>Local successful-query history, shared by all tabs in one workspace window.</summary>
public sealed class SqlQueryHistoryStore
{
    public const int MaxEntries = 30;
    private const int MaxFileBytes = 8_000_000;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public SqlQueryHistoryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Укажите абсолютный путь локальной истории.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<SqlQueryHistoryEntry>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task AppendAsync(SqlQueryHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Validate(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            var previous = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var entries = new[] { entry }.Concat(previous.Where(item => item.Sql != entry.Sql || item.Provider != entry.Provider || item.ConnectionName != entry.ConnectionName)).Take(MaxEntries).ToArray();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(entries);
            if (bytes.Length > MaxFileBytes) throw new IOException("Локальная история превышает допустимый размер.");
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, ".sql-history-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }

    private async Task<IReadOnlyList<SqlQueryHistoryEntry>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > MaxFileBytes) throw new IOException("Файл истории слишком большой.");
        var entries = await JsonSerializer.DeserializeAsync<SqlQueryHistoryEntry[]>(stream,
            new JsonSerializerOptions { MaxDepth = 8 }, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("Файл истории пуст или повреждён.");
        if (entries.Length > MaxEntries) throw new IOException("В файле истории слишком много записей.");
        foreach (var entry in entries) Validate(entry);
        return entries;
    }

    private static void Validate(SqlQueryHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.ConnectionName) || entry.ConnectionName.Length > 512
            || string.IsNullOrWhiteSpace(entry.Provider) || entry.Provider.Length > 100
            || entry.RowCount is < 0 or > SqlReadLimits.MaxRows)
            throw new ArgumentException("Некорректная запись локальной истории.");
        new SqlReadRequest(entry.Sql).Validate();
    }
}

public static class SqlQueryFiles
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var buffer = new byte[SqlReadLimits.MaxSqlBytes + 4];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }
        var offset = length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
        if (length - offset > SqlReadLimits.MaxSqlBytes) throw new IOException("SQL-файл превышает 64 КиБ UTF-8.");
        var sql = StrictUtf8.GetString(buffer, offset, length - offset);
        new SqlReadRequest(sql).Validate();
        return sql;
    }

    public static async Task WriteAsync(string path, string sql, CancellationToken cancellationToken = default)
    {
        new SqlReadRequest(sql).Validate();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temporary = Path.Combine(directory, ".sql-save-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, StrictUtf8.GetBytes(sql), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
