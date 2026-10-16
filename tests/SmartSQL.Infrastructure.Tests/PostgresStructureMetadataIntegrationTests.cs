using Npgsql;
using SmartSQL.Application;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Infrastructure.Tests;

/// <summary>Only uniquely owned objects in the explicitly enabled loopback fixture.</summary>
public sealed class PostgresStructureMetadataIntegrationTests
{
    [DedicatedPostgresFact]
    public async Task QuotedCompoundKeysForeignMappingsChecksAndExpressionIncludeIndexesKeepNativeProperties()
    {
        await using var fixture = await Fixture.CreateAsync();
        var parent = fixture.Table("parent \"odd\"");
        var child = fixture.Table("child \"odd\"");
        await fixture.ExecuteAsync($"""
            CREATE DOMAIN {fixture.Qualified("positive")} AS integer CHECK(VALUE>0);
            CREATE TABLE {fixture.Qualified(parent.Name)} (
              "part a" integer NOT NULL, "part b" text NOT NULL, label text, domain_value {fixture.Qualified("positive")},
              CONSTRAINT "ordered pk" PRIMARY KEY("part b","part a"),
              CONSTRAINT "deferred unique" UNIQUE(label,"part a") DEFERRABLE INITIALLY DEFERRED);
            ALTER TABLE {fixture.Qualified(parent.Name)} ADD CONSTRAINT "unchecked" CHECK("part a">0) NOT VALID;
            CREATE UNIQUE INDEX "expression index" ON {fixture.Qualified(parent.Name)} (lower(label) DESC) INCLUDE("part a") WHERE label IS NOT NULL;
            CREATE TABLE {fixture.Qualified(child.Name)} (id integer, pa integer, pb text,
              CONSTRAINT "ordered fk" FOREIGN KEY(pb,pa) REFERENCES {fixture.Qualified(parent.Name)}("part b","part a")
              MATCH FULL ON UPDATE CASCADE ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED);
            """);
        await fixture.GrantReadAsync(parent.Name, child.Name);
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);

        var structure = await browser.GetStructureAsync(parent);
        Assert.Equal(DatabaseMetadataCoverage.Complete, structure.ConstraintCoverage);
        Assert.Equal(DatabaseMetadataCoverage.Complete, structure.IndexCoverage);
        var primary = Assert.Single(structure.Constraints, item => item.Kind == DatabaseConstraintKind.PrimaryKey);
        Assert.Equal(new[] { "part b", "part a" }, primary.Columns);
        Assert.Equal("ordered pk", primary.Name);
        var unique = Assert.Single(structure.Constraints, item => item.Kind == DatabaseConstraintKind.Unique);
        Assert.True(unique.IsDeferrable);
        Assert.True(unique.IsInitiallyDeferred);
        Assert.True(unique.IsValidated);
        Assert.False(Assert.Single(structure.Constraints, item => item.Kind == DatabaseConstraintKind.Check).IsValidated);
        Assert.Null(structure.NativeDefinition);
        Assert.Contains(structure.Diagnostics, item => item.Code == "table_definition_unavailable");
        Assert.Contains(structure.Diagnostics, item => item.Code == "structure_owned_scope");
        Assert.DoesNotContain(structure.Constraints, item => item.Columns.Contains("domain_value"));

        var index = Assert.Single(structure.Indexes, item => item.Name == "expression index");
        Assert.True(index.IsUnique);
        Assert.True(index.IsPartial);
        Assert.True(index.IsValid);
        Assert.True(index.IsReady);
        Assert.Equal("btree", index.NativeMethod);
        Assert.Contains("WHERE", index.NativeDefinition!);
        Assert.Equal(new[] { 1, 2 }, index.Terms.Select(term => term.Ordinal));
        Assert.Equal(DatabaseIndexTermKind.Expression, index.Terms[0].Kind);
        Assert.Contains("lower", index.Terms[0].NativeDefinition!);
        Assert.True(index.Terms[0].Descending);
        Assert.True(index.Terms[0].IsKey);
        Assert.Equal("part a", index.Terms[1].ColumnName);
        Assert.False(index.Terms[1].IsKey);
        Assert.Null(index.Terms[1].Descending);

        // A relationship may be described without granting access to its target rows.
        await fixture.ExecuteAsync($"REVOKE SELECT ON {fixture.Qualified(parent.Name)} FROM smartsql_browser_reader");
        var foreign = Assert.Single((await browser.GetStructureAsync(child)).Constraints);
        Assert.Equal(DatabaseConstraintKind.ForeignKey, foreign.Kind);
        Assert.Equal(new[] { "pb", "pa" }, foreign.Columns);
        Assert.NotNull(foreign.ForeignKey);
        Assert.Equal(fixture.Schema, foreign.ForeignKey.ReferencedSchema);
        Assert.Equal(parent.Name, foreign.ForeignKey.ReferencedTable);
        Assert.Equal(new[] { "part b", "part a" }, foreign.ForeignKey.ReferencedColumns);
        Assert.Equal("CASCADE", foreign.ForeignKey.OnUpdate);
        Assert.Equal("RESTRICT", foreign.ForeignKey.OnDelete);
        Assert.Equal("FULL", foreign.ForeignKey.Match);
        Assert.True(foreign.IsDeferrable);
        Assert.True(foreign.IsInitiallyDeferred);
        var denied = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetStructureAsync(parent));
        Assert.Equal("permission_denied", denied.Code);
        Assert.Null(denied.InnerException);
    }

    [DedicatedPostgresFact]
    public async Task ViewMaterializedViewAndExclusionDefinitionsArePreservedWithoutInventedTableDdl()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync($"""
            CREATE TABLE {fixture.Qualified("ranges")}(span int4range,
              CONSTRAINT "no overlap" EXCLUDE USING gist(span WITH &&));
            CREATE VIEW {fixture.Qualified("read view")} AS SELECT span FROM {fixture.Qualified("ranges")};
            CREATE MATERIALIZED VIEW {fixture.Qualified("stored view")} AS SELECT span FROM {fixture.Qualified("ranges")};
            CREATE INDEX "stored index" ON {fixture.Qualified("stored view")} USING gist(span);
            """);
        await fixture.GrantReadAsync("ranges", "read view", "stored view");
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var ranges = await browser.GetStructureAsync(fixture.Table("ranges"));
        var exclusion = Assert.Single(ranges.Constraints);
        Assert.Equal(DatabaseConstraintKind.Exclusion, exclusion.Kind);
        Assert.Contains("EXCLUDE USING gist", exclusion.NativeDefinition!);
        Assert.Equal("gist", Assert.Single(ranges.Indexes).NativeMethod);
        foreach (var table in new[] { fixture.Table("read view", DatabaseObjectKind.View), fixture.Table("stored view", DatabaseObjectKind.MaterializedView) })
        {
            var structure = await browser.GetStructureAsync(table);
            Assert.Contains("SELECT", structure.NativeDefinition!);
            Assert.Contains("ranges", structure.NativeDefinition!);
            Assert.Single(structure.Columns);
            Assert.Empty(structure.Constraints);
        }
    }

    [DedicatedPostgresFact]
    public async Task FailedConcurrentUniqueIndexRemainsVisibleAsInvalid()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync($"CREATE TABLE {fixture.Qualified("duplicates")}(value integer); INSERT INTO {fixture.Qualified("duplicates")} VALUES(1),(1)");
        await fixture.GrantReadAsync("duplicates");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync(
            $"CREATE UNIQUE INDEX CONCURRENTLY \"invalid unique\" ON {fixture.Qualified("duplicates")}(value)"));
        Assert.Equal("23505", failure.SqlState);
        await using var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var index = Assert.Single((await browser.GetStructureAsync(fixture.Table("duplicates"))).Indexes);
        Assert.Equal("invalid unique", index.Name);
        Assert.True(index.IsUnique);
        Assert.False(index.IsValid);
        Assert.NotNull(index.IsReady);
    }

    [DedicatedPostgresFact]
    public async Task SuccessfulStructureReadRetainsCurrentGuardedEditHandleAndReadonlySessionCannotWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync($"CREATE TABLE {fixture.Qualified("editable")}(id integer PRIMARY KEY,note text); INSERT INTO {fixture.Qualified("editable")} VALUES(1,'before'); GRANT SELECT,UPDATE ON {fixture.Qualified("editable")} TO smartsql_browser_editor");
        await fixture.GrantReadAsync("editable");
        var table = fixture.Table("editable");
        await using var editor = await PostgresDatabaseBrowser.OpenForEditingAsync(Profile("smartsql_browser_editor"), string.Empty);
        var row = Assert.Single((await editor.ReadPageAsync(table, 0, 10)).Rows);
        Assert.NotNull(row.EditHandle);
        await editor.GetStructureAsync(table);
        await editor.UpdateCellAsync(new(table, row.EditHandle!.Value, "note", "after structure"));
        await using var reader = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        await reader.GetStructureAsync(table);
        var readonlyRow = Assert.Single((await reader.ReadPageAsync(table, 0, 10)).Rows);
        Assert.Equal("after structure", readonlyRow.Values[1]);
        Assert.Null(readonlyRow.EditHandle);
        Assert.False((await reader.GetEditCapabilitiesAsync(table)).CanUpdate);
    }

    [DedicatedPostgresFact]
    public async Task OversizedNativeDefinitionFailsBeforeDisplayAndCancellationDisposalAndMissingObjectAreExplicit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var large = new string('x', DatabaseStructureLimits.MaxTextCharacters + 1);
        await fixture.ExecuteAsync($"CREATE TABLE {fixture.Qualified("huge")}(note text CHECK(note <> '{large}')); CREATE TABLE {fixture.Qualified("normal")}(id integer)");
        await fixture.GrantReadAsync("huge", "normal");
        var browser = await PostgresDatabaseBrowser.OpenAsync(Profile(), string.Empty);
        var error = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetStructureAsync(fixture.Table("huge")));
        Assert.Equal("structure_too_large", error.Code);
        Assert.Single((await browser.GetStructureAsync(fixture.Table("normal"))).Columns);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => browser.GetStructureAsync(fixture.Table("normal"), cancellation.Token));
        var missing = await Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetStructureAsync(fixture.Table("missing \"; SELECT 1;--")));
        Assert.Equal("object_unavailable", missing.Code);
        Assert.Null(missing.InnerException);
        await browser.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => browser.GetStructureAsync(fixture.Table("normal")));
    }

    private static ConnectionProfile Profile(string user = "smartsql_browser_reader")
    {
        if (Environment.GetEnvironmentVariable("SMARTSQL_POSTGRES_TEST_PORT") != "15432")
            throw new InvalidOperationException("Only the dedicated loopback PostgreSQL fixture is permitted.");
        return new(Guid.NewGuid(), "Structure fixture", DatabaseProviders.PostgreSql, "127.0.0.1", 15432,
            "smartsql_browser_test", user, ConnectionSslPolicy.Disable);
    }

    private sealed class Fixture(NpgsqlConnection connection, string schema) : IAsyncDisposable
    {
        public string Schema { get; } = schema;
        public string Qualified(string name) => PostgresBrowserSql.QuoteIdentifier(Schema) + "." + PostgresBrowserSql.QuoteIdentifier(name);
        public DatabaseTableRef Table(string name, DatabaseObjectKind kind = DatabaseObjectKind.Table) => new(Schema, name, kind);

        public static async Task<Fixture> CreateAsync()
        {
            var profile = Profile("smartsql_test_admin");
            var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
            {
                Host = profile.Host, Port = profile.Port, Database = profile.Database, Username = profile.Username,
                Password = string.Empty, SslMode = SslMode.Disable, Pooling = false, Timeout = 5, CommandTimeout = 15
            }.ConnectionString);
            await connection.OpenAsync();
            var fixture = new Fixture(connection, "inspector_" + Guid.NewGuid().ToString("N"));
            try
            {
                await fixture.ExecuteAsync($"CREATE SCHEMA {PostgresBrowserSql.QuoteIdentifier(fixture.Schema)}; GRANT USAGE ON SCHEMA {PostgresBrowserSql.QuoteIdentifier(fixture.Schema)} TO smartsql_browser_reader,smartsql_browser_editor");
                return fixture;
            }
            catch { await connection.DisposeAsync(); throw; }
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public Task GrantReadAsync(params string[] names) => ExecuteAsync("GRANT SELECT ON " + string.Join(",", names.Select(Qualified)) + " TO smartsql_browser_reader");

        public async ValueTask DisposeAsync()
        {
            try { await ExecuteAsync($"DROP SCHEMA {PostgresBrowserSql.QuoteIdentifier(Schema)} CASCADE"); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
