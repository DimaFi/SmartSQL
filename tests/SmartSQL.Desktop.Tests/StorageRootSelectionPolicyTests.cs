using SmartSQL.Desktop.Storage;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class StorageRootSelectionPolicyTests
{
    private static readonly string AppFolder = Path.Combine(Path.GetTempPath(), "smartsql-policy-fixture", "app");
    private static readonly string DefaultFolder = Path.Combine(Path.GetTempPath(), "smartsql-policy-fixture", "installed", "data");
    private static readonly string OverrideFolder = Path.Combine(Path.GetTempPath(), "smartsql-policy-fixture", "override");
    private static readonly Guid Id = Guid.Parse("ebca7b93-09a8-49b7-b798-092fe674298c");

    private static StorageRootSelectionInput Input(
        bool overridePresent = false, string? overrideValue = null,
        bool portablePresent = false, string? portable = null,
        bool bootstrapPresent = false, string? bootstrap = null,
        IReadOnlyList<string>? legacy = null) =>
        new(AppFolder, DefaultFolder, overridePresent, overrideValue,
            portablePresent, portable, bootstrapPresent, bootstrap, legacy);

    private static string Bootstrap(string path, Guid? id = null) =>
        $$"""{"schemaVersion":1,"dataDirectory":{{System.Text.Json.JsonSerializer.Serialize(path)}},"rootId":"{{id ?? Id}}"}""";

    [Fact]
    public void FirstUseIsOnlyACandidateAndDoesNotCreateAProject()
    {
        var marker = Path.Combine(DefaultFolder, "storage-root.json");
        var result = StorageRootSelectionPolicy.Select(Input());
        Assert.Equal(StorageSelectionKind.FirstUseCandidate, result.Kind);
        Assert.Equal(Path.GetFullPath(DefaultFolder), result.DataDirectory);
        Assert.Null(result.ExpectedRootId);
        Assert.Empty(result.LegacyCandidates);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void AbsoluteOverrideHasPriorityOverInvalidPortableBootstrapAndLegacy()
    {
        var result = StorageRootSelectionPolicy.Select(Input(
            overridePresent: true, overrideValue: OverrideFolder,
            portablePresent: true, portable: "{broken",
            bootstrapPresent: true, bootstrap: "{broken", legacy: new[] { AppFolder }));
        Assert.Equal(StorageSelectionKind.ExplicitOverride, result.Kind);
        Assert.Equal(Path.GetFullPath(OverrideFolder), result.DataDirectory);
        Assert.Null(result.ExpectedRootId);
        Assert.Empty(result.LegacyCandidates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("relative-folder")]
    public void PresentInvalidOverrideNeverFallsBack(string? configured)
    {
        Assert.Throws<StorageRootSelectionException>(() => StorageRootSelectionPolicy.Select(
            Input(overridePresent: true, overrideValue: configured,
                portablePresent: true, portable: """{"schemaVersion":1,"dataDirectory":"portable"}""")));
    }

    [Fact]
    public void PortableRelativePathIsResolvedFromMarkerFolderNotWorkingDirectory()
    {
        var result = StorageRootSelectionPolicy.Select(Input(
            portablePresent: true, portable: """{"schemaVersion":1,"dataDirectory":"portable/user-data"}""",
            bootstrapPresent: true, bootstrap: "{broken"));
        Assert.Equal(StorageSelectionKind.Portable, result.Kind);
        Assert.Equal(Path.GetFullPath(Path.Combine(AppFolder, "portable", "user-data")), result.DataDirectory);
        Assert.Null(result.ExpectedRootId);
    }

    [Fact]
    public void PortableAbsolutePathIsSupportedWithoutInstalledBootstrap()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 1, dataDirectory = OverrideFolder });
        var result = StorageRootSelectionPolicy.Select(Input(portablePresent: true, portable: json));
        Assert.Equal(StorageSelectionKind.Portable, result.Kind);
        Assert.Equal(Path.GetFullPath(OverrideFolder), result.DataDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("""{"schemaVersion":2,"dataDirectory":"portable"}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":""}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":15}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":"portable","dataDirectory":"other"}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":"portable","unknown":true}""")]
    public void InvalidPortableMarkerNeverFallsBackToInstalled(string? content)
    {
        Assert.Throws<StorageRootSelectionException>(() => StorageRootSelectionPolicy.Select(
            Input(portablePresent: true, portable: content,
                bootstrapPresent: true, bootstrap: Bootstrap(DefaultFolder))));
    }

    [Fact]
    public void OversizedPortableMarkerIsRejectedWithoutTruncation()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 1, dataDirectory = new string('a', 4100) });
        Assert.Throws<StorageRootSelectionException>(() => StorageRootSelectionPolicy.Select(
            Input(portablePresent: true, portable: json)));
    }

    [Fact]
    public void ValidInstalledBootstrapPreservesTheSelectedRootIdentity()
    {
        var result = StorageRootSelectionPolicy.Select(Input(
            bootstrapPresent: true, bootstrap: Bootstrap(OverrideFolder),
            legacy: new[] { AppFolder }));
        Assert.Equal(StorageSelectionKind.InstalledExisting, result.Kind);
        Assert.Equal(Path.GetFullPath(OverrideFolder), result.DataDirectory);
        Assert.Equal(Id, result.ExpectedRootId);
        Assert.Empty(result.LegacyCandidates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{broken")]
    [InlineData("""{"schemaVersion":2,"dataDirectory":"any","rootId":"ebca7b93-09a8-49b7-b798-092fe674298c"}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":"relative","rootId":"ebca7b93-09a8-49b7-b798-092fe674298c"}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":"/tmp/data","rootId":"not-guid"}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":"/tmp/data","rootId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"schemaVersion":1,"dataDirectory":"/tmp/data","rootId":"ebca7b93-09a8-49b7-b798-092fe674298c","unknown":1}""")]
    public void InvalidInstalledBootstrapNeverFallsBackOrCreatesANewRoot(string? content)
    {
        Assert.Throws<StorageRootSelectionException>(() => StorageRootSelectionPolicy.Select(
            Input(bootstrapPresent: true, bootstrap: content)));
    }

    [Fact]
    public void RootMarkerCanBeValidatedIndependentlyOfBootstrap()
    {
        var parsed = StorageRootSelectionPolicy.ParseRootIdentity(
            """{"schemaVersion":1,"rootId":"ebca7b93-09a8-49b7-b798-092fe674298c"}""");
        Assert.Equal(Id, parsed);
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootSelectionPolicy.ParseRootIdentity(
                """{"schemaVersion":1,"rootId":"not-a-guid"}"""));
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootSelectionPolicy.ParseRootIdentity(
                """{"schemaVersion":1,"rootId":"ebca7b93-09a8-49b7-b798-092fe674298c","rootId":"ebca7b93-09a8-49b7-b798-092fe674298c"}"""));
    }

    [Fact]
    public void MultipleLegacyRootsRequireExplicitChoiceInsteadOfArbitraryWinner()
    {
        var second = Path.Combine(AppFolder, "run-data", "local");
        var result = StorageRootSelectionPolicy.Select(Input(legacy: new[] { AppFolder, second }));
        Assert.Equal(StorageSelectionKind.LegacySelectionRequired, result.Kind);
        Assert.Null(result.DataDirectory);
        Assert.Equal(new[] { Path.GetFullPath(AppFolder), Path.GetFullPath(second) }, result.LegacyCandidates);
    }

    [Fact]
    public void DuplicateLegacyCandidatesRemainOneWithoutChangingFiles()
    {
        var result = StorageRootSelectionPolicy.Select(Input(legacy: new[] { AppFolder, AppFolder }));
        Assert.Single(result.LegacyCandidates);
        Assert.False(Directory.Exists(AppFolder));
    }

    [Fact]
    public void AFileNameOrVolumeRootIsRejectedAsDataDirectory()
    {
        Assert.Throws<StorageRootSelectionException>(() => StorageRootSelectionPolicy.Select(
            Input(overridePresent: true, overrideValue: Path.GetPathRoot(OverrideFolder))));
        foreach (var suffix in new[] { ".db", ".sqlite", "-wal", "-shm", "-journal" })
        {
            Assert.Throws<StorageRootSelectionException>(() => StorageRootSelectionPolicy.Select(
                Input(overridePresent: true, overrideValue: OverrideFolder + suffix)));
        }
    }

    [Fact]
    public void SelectionDoesNotRequireTheDirectoryToExistAndDoesNotCreateIt()
    {
        var neverCreated = Path.Combine(DefaultFolder, Guid.NewGuid().ToString("N"));
        var result = StorageRootSelectionPolicy.Select(Input(
            overridePresent: true, overrideValue: neverCreated));
        Assert.Equal(Path.GetFullPath(neverCreated), result.DataDirectory);
        Assert.False(Directory.Exists(neverCreated));
    }
}
