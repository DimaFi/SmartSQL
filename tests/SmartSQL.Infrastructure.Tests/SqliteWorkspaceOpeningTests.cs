using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Infrastructure;

namespace SmartSQL.Infrastructure.Tests;

public sealed class SqliteWorkspaceOpeningTests
{
    [Xunit.Fact]
    public void OpeningNonSqliteBytesPreservesOriginal()
    {
        var directory = NewDirectoryPath();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "corrupt.db");
        File.WriteAllText(path, "This is not SQLite: keep the original bytes.");
        var hash = Hash(path);

        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(path));

        Xunit.Assert.Equal(hash, Hash(path));
        Xunit.Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Theory]
    [Xunit.InlineData("-journal")]
    [Xunit.InlineData("-wal")]
    [Xunit.InlineData("-shm")]
    public void CreateNewDoesNotAttachOrDeleteExistingSidecar(string suffix)
    {
        var directory = NewDirectoryPath();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "project.db");
        File.WriteAllText(path + suffix, "owned by another SQLite operation");
        var hash = Hash(path + suffix);

        Xunit.Assert.Throws<IOException>(() => SqliteWorkspace.CreateNew(path));

        Xunit.Assert.Equal(hash, Hash(path + suffix));
        Xunit.Assert.False(File.Exists(path));
        Xunit.Assert.Equal([path + suffix], Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Fact]
    public void MetadataTriggerIsRejectedWithoutMigrationOrWrites()
    {
        var directory = NewDirectoryPath();
        var path = Path.Combine(directory, "project.db");
        SqliteWorkspace.CreateNew(path);
        ClearWorkspacePool(path);
        Execute(path, "CREATE TABLE sentinel(value TEXT); CREATE TRIGGER unexpected AFTER INSERT ON smartsql_tables BEGIN INSERT INTO sentinel VALUES('changed'); END;");
        var hash = Hash(path);

        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(path));

        Xunit.Assert.Equal(hash, Hash(path));
        Xunit.Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Fact]
    public void OpeningMissingFileDoesNotCreateFileOrParentDirectory()
    {
        var directory = NewDirectoryPath();
        var path = Path.Combine(directory, "missing-parent", "project.db");

        Xunit.Assert.Throws<FileNotFoundException>(() => SqliteWorkspace.OpenExisting(path));

        Xunit.Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "missing.db");
        Xunit.Assert.Throws<FileNotFoundException>(() => SqliteWorkspace.OpenExisting(path));
        Xunit.Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Theory]
    [Xunit.InlineData(false)]
    [Xunit.InlineData(true)]
    public void OpeningEmptyFileDoesNotInitializeIt(bool sqliteHeader)
    {
        var directory = NewDirectoryPath();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "empty.db");
        if (sqliteHeader)
            Execute(path, "PRAGMA user_version = 0; VACUUM;");
        else
            File.WriteAllBytes(path, []);
        var hash = Hash(path);

        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(path));

        Xunit.Assert.Equal(hash, Hash(path));
        Xunit.Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Theory]
    [Xunit.InlineData("CREATE TABLE ordinary(id INTEGER PRIMARY KEY, value TEXT); INSERT INTO ordinary VALUES(1, 'preserve me');")]
    [Xunit.InlineData("CREATE VIEW ordinary AS SELECT 'preserve me' AS value;")]
    [Xunit.InlineData("CREATE TABLE smartsql_tables(unrelated TEXT); INSERT INTO smartsql_tables VALUES('preserve me');")]
    [Xunit.InlineData("CREATE VIEW smartsql_tables AS SELECT 'preserve me' AS id, 'ordinary' AS name;")]
    [Xunit.InlineData("CREATE TABLE smartsql_tables(id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE);")]
    [Xunit.InlineData("CREATE TABLE smartsql_tables(id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE); PRAGMA user_version = 1;")]
    [Xunit.InlineData("CREATE TABLE smartsql_tables(id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE); PRAGMA user_version = 2;")]
    [Xunit.InlineData("CREATE TABLE smartsql_tables(id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE); PRAGMA user_version = 3;")]
    [Xunit.InlineData("CREATE TABLE smartsql_tables(id TEXT); CREATE TABLE smartsql_fields(field_type TEXT); CREATE TABLE smartsql_saved_filters(id TEXT); CREATE TABLE smartsql_saved_filter_items(id TEXT); PRAGMA user_version = 3;")]
    public void OpeningForeignOrIncompleteSchemaPreservesSourceAndCreatesNoBackup(string sql)
    {
        var directory = NewDirectoryPath();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ordinary.db");
        Execute(path, sql);
        var schema = ReadSchemaAndVersion(path);
        var hash = Hash(path);

        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(path));

        Xunit.Assert.Equal(schema, ReadSchemaAndVersion(path));
        Xunit.Assert.Equal(hash, Hash(path));
        // Closed rollback-journal fixtures have no pre-existing WAL/SHM to recover.
        Xunit.Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Fact]
    public void OpeningFutureManagedVersionPreservesDataAndCreatesNoBackup()
    {
        var directory = NewDirectoryPath();
        var path = Path.Combine(directory, "future.db");
        var workspace = SqliteWorkspace.CreateNew(path);
        var table = workspace.CreateTable("Existing", ["Value"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "preserve me" });
        ClearWorkspacePool(path);
        Execute(path, "PRAGMA user_version = 999;");
        var hash = Hash(path);
        var schema = ReadSchemaAndVersion(path);

        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(path));

        Xunit.Assert.Equal(hash, Hash(path));
        Xunit.Assert.Equal(schema, ReadSchemaAndVersion(path));
        Xunit.Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Theory]
    [Xunit.InlineData("")]
    [Xunit.InlineData("not a database")]
    public void CreateNewRefusesExistingFileWithoutOverwritingIt(string content)
    {
        var directory = NewDirectoryPath();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "existing.db");
        File.WriteAllText(path, content);
        var hash = Hash(path);

        Xunit.Assert.Throws<IOException>(() => SqliteWorkspace.CreateNew(path));

        Xunit.Assert.Equal(hash, Hash(path));
        Xunit.Assert.Equal([path], Directory.GetFileSystemEntries(directory));
    }

    [Xunit.Fact]
    public void CreateNewRefusesExistingManagedDatabaseAndKeepsRecords()
    {
        var path = Path.Combine(NewDirectoryPath(), "project.db");
        var workspace = SqliteWorkspace.CreateNew(path);
        var table = workspace.CreateTable("Existing", ["Value"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "preserve me" });
        ClearWorkspacePool(path);
        var hash = Hash(path);

        for (var attempt = 0; attempt < 3; attempt++)
            Xunit.Assert.Throws<IOException>(() => SqliteWorkspace.CreateNew(path));

        Xunit.Assert.Equal(hash, Hash(path));
        var reopened = SqliteWorkspace.OpenExisting(path);
        Xunit.Assert.Equal("preserve me", Xunit.Assert.Single(reopened.GetRecords(table.Id)).Values[table.Fields[0].Id]);
        Xunit.Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.before-upgrade-*.db"));
    }

    [Xunit.Fact]
    public async Task ConcurrentCreateHasOneWinnerAndOneCollision()
    {
        var path = Path.Combine(NewDirectoryPath(), "project.db");
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        Task<Exception?> Attempt() => Task.Run<Exception?>(() =>
        {
            ready.Signal();
            start.Wait();
            return Xunit.Record.Exception(() => SqliteWorkspace.CreateNew(path));
        });
        var first = Attempt();
        var second = Attempt();
        try
        {
            Xunit.Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            start.Set();
        }
        var results = await Task.WhenAll(first, second);

        Xunit.Assert.Single(results, exception => exception is null);
        Xunit.Assert.IsType<IOException>(Xunit.Assert.Single(results, exception => exception is not null));
        Xunit.Assert.Empty(SqliteWorkspace.OpenExisting(path).GetTables());
    }

    [Xunit.Theory]
    [Xunit.InlineData(false)]
    [Xunit.InlineData(true)]
    public void OperationAfterSourceDeletionFailsWithoutRecreatingDatabase(bool write)
    {
        var path = Path.Combine(NewDirectoryPath(), "project.db");
        var workspace = SqliteWorkspace.CreateNew(path);
        workspace.CreateTable("Existing", ["Value"]);
        ClearWorkspacePool(path);
        File.Delete(path);

        Xunit.Assert.Throws<SqliteException>(() =>
        {
            if (write)
                workspace.CreateTable("Later", ["Value"]);
            else
                workspace.GetTables();
        });

        Xunit.Assert.False(File.Exists(path));
        Xunit.Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!));
    }

    private static string NewDirectoryPath() =>
        Path.Combine(TestPaths.Root, "smartsql-opening-tests", Guid.NewGuid().ToString("N"));

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ReadSchemaAndVersion(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_schema ORDER BY type, name";
        using var reader = command.ExecuteReader();
        var entries = new List<string>();
        while (reader.Read())
            entries.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
        reader.Close();
        command.CommandText = "PRAGMA user_version";
        entries.Add($"version={command.ExecuteScalar()}");
        return string.Join("\n", entries);
    }

    private static void ClearWorkspacePool(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = true
        }.ToString());
        SqliteConnection.ClearPool(connection);
    }
}
