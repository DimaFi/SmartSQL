using System.Text;
using System.Text.Json;
using SmartSQL.Application;
using SmartSQL.Desktop.Queries;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class SqlQueryHistoryTests
{
    [Fact]
    public async Task HistoryIsBoundedAcrossConcurrentTabsDeduplicatesAndSurvivesReopen()
    {
        using var directory = new Fixture();
        var path = Path.Combine(directory.Path, "history.json");
        var history = new SqlQueryHistoryStore(path);
        await Task.WhenAll(Enumerable.Range(1, 45).Select(i => history.AppendAsync(Entry("SELECT " + i))));
        var entries = await new SqlQueryHistoryStore(path).ReadAsync();
        Assert.Equal(30, entries.Count);
        var repeated = entries[^1] with { ExecutedUtc = DateTimeOffset.UtcNow, RowCount = 2 };
        await history.AppendAsync(repeated);
        entries = await history.ReadAsync();
        Assert.Equal(repeated, entries[0]);
        Assert.Equal(30, entries.Count);
        Assert.Single(entries, item => item.Sql == repeated.Sql);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CorruptHistoryIsReportedAndNeverSilentlyOverwritten()
    {
        using var directory = new Fixture();
        var path = Path.Combine(directory.Path, "history.json");
        await File.WriteAllTextAsync(path, "{broken");
        var history = new SqlQueryHistoryStore(path);
        await Assert.ThrowsAsync<JsonException>(() => history.AppendAsync(Entry("SELECT 1")));
        Assert.Equal("{broken", await File.ReadAllTextAsync(path));
        await File.WriteAllTextAsync(path, new string('x', 8_000_001));
        await Assert.ThrowsAsync<IOException>(() => history.ReadAsync());
    }

    [Fact]
    public async Task SqlFilesPreserveUnicodeLiteralWhitespaceAndBomWithoutRunningAnything()
    {
        using var directory = new Fixture();
        var path = Path.Combine(directory.Path, "query.sql");
        const string text = "-- Привет 🌍\r\nSELECT '  ; -- ' AS duplicate, NULL AS duplicate;\r\n";
        await SqlQueryFiles.WriteAsync(path, text);
        Assert.Equal(text, await SqlQueryFiles.ReadAsync(path));
        await File.WriteAllBytesAsync(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray());
        Assert.Equal(text, await SqlQueryFiles.ReadAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task InvalidOrOversizeSqlFilesAreRejectedAndFailedSavePreservesOriginal()
    {
        using var directory = new Fixture();
        var path = Path.Combine(directory.Path, "query.sql");
        await File.WriteAllBytesAsync(path, [0xFF]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => SqlQueryFiles.ReadAsync(path));
        await File.WriteAllTextAsync(path, new string('я', 32_769));
        await Assert.ThrowsAsync<IOException>(() => SqlQueryFiles.ReadAsync(path));
        await File.WriteAllTextAsync(path, "SELECT 1");
        await Assert.ThrowsAsync<ArgumentException>(() => SqlQueryFiles.WriteAsync(path, "SELECT '\0'"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SqlQueryFiles.WriteAsync(path, "SELECT 2", cancellation.Token));
        Assert.Equal("SELECT 1", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static SqlQueryHistoryEntry Entry(string sql) => new(DateTimeOffset.UtcNow, "Synthetic fixture", "SQLite", sql, 1, false);
    private sealed class Fixture : IDisposable
    {
        public string Path { get; }
        public Fixture()
        {
            var root = Environment.GetEnvironmentVariable("SMARTSQL_TEST_DIRECTORY") ?? throw new InvalidOperationException("Use scripts/Test.ps1.");
            Path = System.IO.Path.Combine(root, "sql-history", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
