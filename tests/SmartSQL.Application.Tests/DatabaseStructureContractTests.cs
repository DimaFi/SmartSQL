using SmartSQL.Domain;

namespace SmartSQL.Application.Tests;

public sealed class DatabaseStructureContractTests
{
    private static readonly DatabaseTableRef Table = new("main", "data", DatabaseObjectKind.Table);

    [Xunit.Fact]
    public async Task LegacyBrowserReportsUnconfirmedSectionsInsteadOfCompleteEmptyMetadata()
    {
        var legacy = new ColumnsOnlyBrowser();
        IDatabaseBrowser browser = legacy;
        using var cancellation = new CancellationTokenSource();
        var structure = await browser.GetStructureAsync(Table, cancellation.Token);

        Xunit.Assert.Same(legacy.Columns, structure.Columns);
        Xunit.Assert.Equal(cancellation.Token, legacy.ReceivedToken);
        Xunit.Assert.Equal(Table, structure.Table);
        Xunit.Assert.Equal(DatabaseMetadataCoverage.Unavailable, structure.ConstraintCoverage);
        Xunit.Assert.Equal(DatabaseMetadataCoverage.Unavailable, structure.IndexCoverage);
        Xunit.Assert.Empty(structure.Constraints);
        Xunit.Assert.Empty(structure.Indexes);
        Xunit.Assert.Null(structure.NativeDefinition);
        Xunit.Assert.Contains(structure.Diagnostics, diagnostic => diagnostic.Code == "structure_unsupported");
    }

    [Xunit.Fact]
    public async Task PreCancelledInspectorDoesNotStartProviderWork()
    {
        var legacy = new ColumnsOnlyBrowser();
        IDatabaseBrowser browser = legacy;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => browser.GetStructureAsync(Table, cancellation.Token));
        Xunit.Assert.False(legacy.ReadStarted);
    }

    [Xunit.Fact]
    public async Task LegacyBrowserCannotBypassInspectorBounds()
    {
        IDatabaseBrowser browser = new ColumnsOnlyBrowser
        {
            Columns = [new DatabaseColumn("data", 0, new string('x', DatabaseStructureLimits.MaxTextCharacters + 1), true, null)]
        };
        var error = await Xunit.Assert.ThrowsAsync<DatabaseBrowserException>(() => browser.GetStructureAsync(Table));
        Xunit.Assert.Equal("structure_too_large", error.Code);
    }

    [Xunit.Fact]
    public void AggregateBoundRejectsManyIndividuallyValidDefinitions()
    {
        var definition = new string('x', DatabaseStructureLimits.MaxTextCharacters);
        var structure = Empty() with
        {
            Constraints = Enumerable.Range(0, 9).Select(i => new DatabaseConstraint($"c{i}",
                DatabaseConstraintKind.Other, "native", [], definition)).ToArray()
        };
        RejectOversized(structure);
    }

    [Xunit.Theory]
    [Xunit.InlineData("columns")]
    [Xunit.InlineData("constraints")]
    [Xunit.InlineData("indexes")]
    [Xunit.InlineData("diagnostics")]
    [Xunit.InlineData("constraintColumns")]
    [Xunit.InlineData("foreignColumns")]
    [Xunit.InlineData("indexTerms")]
    public void MetadataCollectionsHaveExplicitBounds(string section)
    {
        var structure = Empty();
        structure = section switch
        {
            "columns" => structure with { Columns = Enumerable.Repeat(new DatabaseColumn("c", 0, "text", true, null), DatabaseStructureLimits.MaxColumns + 1).ToArray() },
            "constraints" => structure with { Constraints = Enumerable.Repeat(new DatabaseConstraint(null, DatabaseConstraintKind.Check, "check", []), DatabaseStructureLimits.MaxItems + 1).ToArray() },
            "indexes" => structure with { Indexes = Enumerable.Repeat(new DatabaseIndex("i", false, false, false, []), DatabaseStructureLimits.MaxItems + 1).ToArray() },
            "diagnostics" => structure with { Diagnostics = Enumerable.Repeat(new DatabaseDiagnostic("info", "scope"), DatabaseStructureLimits.MaxItems + 1).ToArray() },
            "constraintColumns" => structure with { Constraints = [new(null, DatabaseConstraintKind.PrimaryKey, "pk", Enumerable.Repeat("c", DatabaseStructureLimits.MaxTerms + 1).ToArray())] },
            "foreignColumns" => structure with { Constraints = [new(null, DatabaseConstraintKind.ForeignKey, "fk", [], ForeignKey: new(null, "target", Enumerable.Repeat<string?>(null, DatabaseStructureLimits.MaxTerms + 1).ToArray(), null, null, null))] },
            "indexTerms" => structure with { Indexes = [new("i", false, false, false, Enumerable.Repeat(new DatabaseIndexTerm(0, DatabaseIndexTermKind.Expression, null, null, true), DatabaseStructureLimits.MaxTerms + 1).ToArray())] },
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        RejectOversized(structure);
    }

    private static DatabaseStructure Empty() => new(Table, [], DatabaseMetadataCoverage.Partial, [],
        DatabaseMetadataCoverage.Partial, [], null, []);

    private static void RejectOversized(DatabaseStructure structure)
    {
        var error = Xunit.Assert.Throws<DatabaseBrowserException>(() => DatabaseStructureLimits.Validate(structure));
        Xunit.Assert.Equal("structure_too_large", error.Code);
    }

    private sealed class ColumnsOnlyBrowser : IDatabaseBrowser
    {
        public IReadOnlyList<DatabaseColumn> Columns { get; init; } = [new("payload", 0, "custom native type", true, null)];
        public bool ReadStarted { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }

        public Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(DatabaseTableRef table, CancellationToken cancellationToken = default)
        {
            ReadStarted = true;
            ReceivedToken = cancellationToken;
            return Task.FromResult(Columns);
        }

        public Task<IReadOnlyList<DatabaseTableRef>> GetTablesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
