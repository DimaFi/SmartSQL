using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

public sealed class NativeSqliteStructureTests
{
    static NativeSqliteStructureTests() => SQLitePCL.Batteries_V2.Init();

    [Fact]
    public async Task Utf16StructurePreservesNativeSqlAndReportsKeysForeignKeysAndOrderedIndexTerms()
    {
        using var file = new Fixture();
        file.Execute(""""
            PRAGMA encoding='UTF-16le';
            CREATE TABLE "parent ""odd"""(
                "part a" INTEGER NOT NULL,
                "part b" TEXT NOT NULL,
                "label" TEXT,
                CONSTRAINT "named pk" PRIMARY KEY("part a","part b"),
                CONSTRAINT "named unique" UNIQUE("label","part b"),
                CHECK(length("label")>0)
            ) WITHOUT ROWID;
            CREATE UNIQUE INDEX "ux expression ""odd""" ON "parent ""odd"""(lower("label"),"part b" DESC)
                WHERE "label" IS NOT NULL;
            CREATE INDEX "ix plain" ON "parent ""odd"""("part a","label");
            CREATE TABLE "child ""odd"""(
                id INTEGER PRIMARY KEY,
                pa INTEGER,
                pb TEXT,
                note TEXT,
                UNIQUE(note,pa),
                FOREIGN KEY(pa,pb) REFERENCES "parent ""odd"""
                    ON UPDATE CASCADE ON DELETE RESTRICT
            );
            CREATE VIEW "view ""odd""" AS SELECT "part a","label" FROM "parent ""odd""";
            """");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var tables = await browser.GetTablesAsync();

        var parent = await browser.GetStructureAsync(tables.Single(table => table.Name == "parent \"odd\""));
        Assert.Equal(DatabaseMetadataCoverage.Partial, parent.ConstraintCoverage);
        Assert.Equal(DatabaseMetadataCoverage.Partial, parent.IndexCoverage);
        Assert.NotNull(parent.NativeDefinition);
        Assert.Contains("CHECK(length(\"label\")>0)", parent.NativeDefinition!, StringComparison.Ordinal);
        var primary = Assert.Single(parent.Constraints, item => item.Kind == DatabaseConstraintKind.PrimaryKey);
        Assert.Null(primary.Name);
        Assert.Equal(new[] { "part a", "part b" }, primary.Columns);
        var unique = Assert.Single(parent.Constraints, item => item.Kind == DatabaseConstraintKind.Unique);
        Assert.Null(unique.Name);
        Assert.Equal(new[] { "label", "part b" }, unique.Columns);

        var expression = Assert.Single(parent.Indexes, item => item.Name == "ux expression \"odd\"");
        Assert.True(expression.IsUnique);
        Assert.True(expression.IsPartial);
        Assert.False(expression.IsPrimary);
        Assert.Null(expression.NativeMethod);
        Assert.Null(expression.IsValid);
        Assert.Null(expression.IsReady);
        Assert.NotNull(expression.NativeDefinition);
        Assert.Contains("lower(\"label\")", expression.NativeDefinition!, StringComparison.Ordinal);
        var expressionKeys = expression.Terms.Where(term => term.IsKey).ToArray();
        Assert.Equal(new[] { 1, 2 }, expressionKeys.Select(term => term.Ordinal));
        Assert.Equal(DatabaseIndexTermKind.Expression, expressionKeys[0].Kind);
        Assert.Null(expressionKeys[0].ColumnName);
        Assert.Null(expressionKeys[0].NativeDefinition);
        Assert.Equal(DatabaseIndexTermKind.Column, expressionKeys[1].Kind);
        Assert.Equal("part b", expressionKeys[1].ColumnName);
        Assert.True(expressionKeys[1].Descending);
        Assert.Contains(expression.Terms, term => !term.IsKey);
        Assert.Contains(parent.Diagnostics, diagnostic => diagnostic.Code == "sqlite_index_expression");

        var child = await browser.GetStructureAsync(tables.Single(table => table.Name == "child \"odd\""));
        var foreign = Assert.Single(child.Constraints, item => item.Kind == DatabaseConstraintKind.ForeignKey);
        Assert.Null(foreign.Name);
        Assert.Equal(new[] { "pa", "pb" }, foreign.Columns);
        Assert.NotNull(foreign.ForeignKey);
        Assert.Equal("main", foreign.ForeignKey!.ReferencedSchema);
        Assert.Equal("parent \"odd\"", foreign.ForeignKey.ReferencedTable);
        Assert.Equal(new string?[] { null, null }, foreign.ForeignKey.ReferencedColumns);
        Assert.Equal("CASCADE", foreign.ForeignKey.OnUpdate);
        Assert.Equal("RESTRICT", foreign.ForeignKey.OnDelete);
        Assert.Contains(child.Diagnostics, diagnostic => diagnostic.Code == "sqlite_implicit_fk_target");
        var automatic = Assert.Single(child.Indexes, index => index.Name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal));
        Assert.Null(automatic.NativeDefinition);
        Assert.Contains(automatic.Terms, term => term.Kind == DatabaseIndexTermKind.RowIdentifier && !term.IsKey);

        var view = await browser.GetStructureAsync(tables.Single(table => table.Name == "view \"odd\""));
        Assert.Equal(DatabaseMetadataCoverage.Unavailable, view.ConstraintCoverage);
        Assert.Equal(DatabaseMetadataCoverage.Unavailable, view.IndexCoverage);
        Assert.Empty(view.Constraints);
        Assert.Empty(view.Indexes);
        Assert.NotNull(view.NativeDefinition);
        Assert.Contains("CREATE VIEW", view.NativeDefinition!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(view.Diagnostics, diagnostic => diagnostic.Code == "sqlite_view_structure");

        DatabaseStructureLimits.Validate(parent);
        DatabaseStructureLimits.Validate(child);
        DatabaseStructureLimits.Validate(view);
        Assert.Equal("UTF-16le", file.Scalar("PRAGMA encoding"));
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task SuccessfulStructureReadKeepsExistingEditHandleUsableForOneCellUpdate()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); INSERT INTO data VALUES(1,'before'),(2,'unchanged')");
        await using var browser = await NativeSqliteDatabaseBrowser.OpenEditableAsync(file.Path, file.BackupDirectory);
        var table = Assert.Single(await browser.GetTablesAsync());
        var selected = (await browser.ReadPageAsync(table, 0, 10)).Rows[0];
        Assert.NotNull(selected.EditHandle);

        var structure = await browser.GetStructureAsync(table);
        Assert.Contains(structure.Constraints, constraint => constraint.Kind == DatabaseConstraintKind.PrimaryKey);
        await browser.UpdateCellAsync(new(table, selected.EditHandle!.Value, "note", "after structure"));

        Assert.Equal(1L, file.Scalar("SELECT count(*) FROM data WHERE id=1 AND note='after structure'"));
        Assert.Equal(1L, file.Scalar("SELECT count(*) FROM data WHERE id=2 AND note='unchanged'"));
        Assert.Single(Directory.GetFiles(file.BackupDirectory, "*.db", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task MalformedNativeSchemaTextIsRejectedWithoutReplacementOrSourceChanges()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT); PRAGMA writable_schema=ON; UPDATE sqlite_schema SET sql=CAST(CAST(sql AS BLOB)||x'2F2AFF2A2F' AS TEXT) WHERE name='data'; PRAGMA writable_schema=OFF");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = Assert.Single(await browser.GetTablesAsync());
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetStructureAsync(table));
        Assert.Equal("unsupported_text_encoding", error.Code);
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task AggregateIndexDefinitionLimitRejectsIndividuallyValidTextsWithoutTruncation()
    {
        using var file = new Fixture();
        file.Execute("CREATE TABLE data(id INTEGER PRIMARY KEY,note TEXT)");
        var literal = new string('x', 900_000);
        for (var i = 0; i < 9; i++)
            file.Execute($"CREATE INDEX ix{i} ON data(note) WHERE note <> '{literal}'");
        var hash = file.Hash();
        await using var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = Assert.Single(await browser.GetTablesAsync());
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetStructureAsync(table));
        Assert.Equal("structure_too_large", error.Code);
        Assert.Equal(hash, file.Hash());
    }

    [Fact]
    public async Task OversizedDefinitionCancellationAndDisposalFailExplicitly()
    {
        using var file = new Fixture();
        var oversized = new string('x', DatabaseStructureLimits.MaxTextCharacters + 1);
        file.Execute($"CREATE TABLE huge(value TEXT DEFAULT '{oversized}')");
        var hash = file.Hash();
        var browser = await NativeSqliteDatabaseBrowser.OpenAsync(file.Path);
        var table = Assert.Single(await browser.GetTablesAsync());
        var tooLarge = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetStructureAsync(table));
        Assert.Equal("structure_too_large", tooLarge.Code);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => browser.GetStructureAsync(table, cancellation.Token));
        await browser.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => browser.GetStructureAsync(table));
        Assert.Equal(hash, file.Hash());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(TestPaths.Root, "native-sqlite-structure", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public string BackupDirectory { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "ordinary.db");
            BackupDirectory = System.IO.Path.Combine(_directory, "backups");
        }

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public object? Scalar(string sql)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        public string Hash()
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
