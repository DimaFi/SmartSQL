using System.Text;
using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>User-authored SQL for explicit read-only execution. Providers parse and enforce read-only policy.</summary>
public interface IReadOnlySqlExecutor : IAsyncDisposable
{
    Task<SqlReadResult> ExecuteAsync(SqlReadRequest request, CancellationToken cancellationToken = default);
}

public sealed record SqlReadRequest(string Sql, int RowLimit = 100)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Sql);
        if (RowLimit is < 1 or > SqlReadLimits.MaxRows)
            throw new ArgumentOutOfRangeException(nameof(RowLimit), $"Лимит строк должен быть от 1 до {SqlReadLimits.MaxRows}.");
        DatabaseBrowseQuery.ValidateText(Sql, nameof(Sql));
        if (Encoding.UTF8.GetByteCount(Sql) > SqlReadLimits.MaxSqlBytes)
            throw new ArgumentException("SQL ограничен 64 КиБ в UTF-8.", nameof(Sql));
    }
}

/// <summary>Expressions may have duplicate names and no declared type. Ordinal is the stable result identity.</summary>
public sealed record SqlReadColumn(int Ordinal, string Name, string? NativeType);

/// <summary>Null and empty text stay distinct. SQLite may report actual per-cell storage class.</summary>
public sealed record SqlReadCell(string? Value, string? StorageClass = null);
public sealed record SqlReadRow(IReadOnlyList<SqlReadCell> Cells);

/// <summary>No table identity, primary key, or editable handles are inferred from an arbitrary query.</summary>
public sealed record SqlReadResult(IReadOnlyList<SqlReadColumn> Columns, IReadOnlyList<SqlReadRow> Rows,
    bool Truncated, IReadOnlyList<DatabaseDiagnostic> Diagnostics);

public static class SqlReadLimits
{
    public const int MaxSqlBytes = 65_536;
    public const int MaxRows = 500;
    public const int MaxColumns = 600;
    public const int MaxCellCharacters = 1_000_000;
    public const int MaxResultCharacters = 16_000_000;
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(15);
}
