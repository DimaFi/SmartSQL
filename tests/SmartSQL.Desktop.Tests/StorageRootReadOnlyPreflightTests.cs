using System.Text;
using System.Text.Json;
using SmartSQL.Desktop.Storage;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class StorageRootReadOnlyPreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(TestRoot(), "storage-preflight", Guid.NewGuid().ToString("N"));
    private string AppDirectory => Path.Combine(_root, "app");
    private string InstalledDirectory => Path.Combine(_root, "installed");
    private string BootstrapPath => Path.Combine(InstalledDirectory, "storage.json");
    private string StoredRoot => Path.Combine(_root, "data");
    private string FirstUsePath => Path.Combine(_root, "first-use");
    private static readonly Guid Id = Guid.Parse("cb8e1743-12c4-4254-b2a6-12c4a0ebbf12");

    private StorageRootPreflightInput Input(
        bool overridePresent = false,
        string? overrideValue = null,
        IReadOnlyList<string>? legacy = null,
        bool legacyCompleted = false) =>
        new(AppDirectory, FirstUsePath, BootstrapPath,
            overridePresent, overrideValue, legacy, legacyCompleted);

    private void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
    }

    private void PrepareInstalled(Guid? rootMarkerId = null)
    {
        Write(BootstrapPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            dataDirectory = StoredRoot,
            rootId = Id.ToString("D")
        }));
        if (rootMarkerId is not null)
            Write(Path.Combine(StoredRoot, "storage-root.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                rootId = rootMarkerId.Value.ToString("D")
            }));
    }

    [Fact]
    public void InstalledBootstrapWithMatchingRootMarkerIsSelectedReadOnly()
    {
        PrepareInstalled(Id);
        var bootstrapBytes = File.ReadAllBytes(BootstrapPath);
        var markerFile = Path.Combine(StoredRoot, "storage-root.json");
        var markerBytes = File.ReadAllBytes(markerFile);

        var selection = StorageRootReadOnlyPreflight.Inspect(Input());

        Assert.Equal(StorageSelectionKind.InstalledExisting, selection.Kind);
        Assert.Equal(Path.GetFullPath(StoredRoot), selection.DataDirectory);
        Assert.Equal(Id, selection.ExpectedRootId);
        Assert.Equal(bootstrapBytes, File.ReadAllBytes(BootstrapPath));
        Assert.Equal(markerBytes, File.ReadAllBytes(markerFile));
        Assert.False(Directory.Exists(FirstUsePath));
    }

    [Fact]
    public void MissingRootIdentityNeverCreatesAFallbackRoot()
    {
        PrepareInstalled();
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootReadOnlyPreflight.Inspect(Input(legacyCompleted: true)));
        Assert.True(File.Exists(BootstrapPath));
        Assert.False(File.Exists(Path.Combine(StoredRoot, "storage-root.json")));
        Assert.False(Directory.Exists(FirstUsePath));
    }

    [Fact]
    public void DifferentRootIdRefusesWithoutRewritingExistingMetadata()
    {
        PrepareInstalled(Guid.Parse("a432d4ba-8e88-4c3f-b3b1-89c32c393070"));
        var bootstrapBytes = File.ReadAllBytes(BootstrapPath);
        var markerPath = Path.Combine(StoredRoot, "storage-root.json");
        var markerBytes = File.ReadAllBytes(markerPath);
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootReadOnlyPreflight.Inspect(Input()));
        Assert.Equal(bootstrapBytes, File.ReadAllBytes(BootstrapPath));
        Assert.Equal(markerBytes, File.ReadAllBytes(markerPath));
    }

    [Fact]
    public void PortableHasPriorityOverCorruptInstalledBootstrap()
    {
        Write(BootstrapPath, "{invalid");
        Write(Path.Combine(AppDirectory, "smartsql.portable.json"),
            """{"schemaVersion":1,"dataDirectory":"portable-data"}""");

        var selection = StorageRootReadOnlyPreflight.Inspect(Input());
        Assert.Equal(StorageSelectionKind.Portable, selection.Kind);
        Assert.Equal(Path.GetFullPath(Path.Combine(AppDirectory, "portable-data")), selection.DataDirectory);
        Assert.Equal("{invalid", File.ReadAllText(BootstrapPath));
        Assert.False(Directory.Exists(FirstUsePath));
    }

    [Fact]
    public void ExplicitOverrideHasPriorityAndDoesNotReadCorruptManifests()
    {
        Write(BootstrapPath, "{invalid");
        Write(Path.Combine(AppDirectory, "smartsql.portable.json"), "{also-invalid");
        var chosen = Path.Combine(_root, "override");
        var selection = StorageRootReadOnlyPreflight.Inspect(
            Input(overridePresent: true, overrideValue: chosen));
        Assert.Equal(StorageSelectionKind.ExplicitOverride, selection.Kind);
        Assert.Equal(Path.GetFullPath(chosen), selection.DataDirectory);
        Assert.False(Directory.Exists(chosen));
    }

    [Fact]
    public void InvalidPresentOverrideDoesNotFallBackToPortable()
    {
        Write(Path.Combine(AppDirectory, "smartsql.portable.json"),
            """{"schemaVersion":1,"dataDirectory":"portable-data"}""");
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootReadOnlyPreflight.Inspect(Input(overridePresent: true, overrideValue: "")));
        Assert.False(Directory.Exists(Path.Combine(AppDirectory, "portable-data")));
    }

    [Fact]
    public void MalformedPortableNeverFallsBackToAbsentBootstrap()
    {
        Write(Path.Combine(AppDirectory, "smartsql.portable.json"), """{"schemaVersion":99}""");
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootReadOnlyPreflight.Inspect(Input(legacyCompleted: true)));
        Assert.False(File.Exists(BootstrapPath));
        Assert.False(Directory.Exists(FirstUsePath));
    }

    [Fact]
    public void CorruptedInstalledBootstrapCannotBeInterpretedAsFirstUse()
    {
        Write(BootstrapPath, "{corrupted");
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootReadOnlyPreflight.Inspect(Input(legacyCompleted: true)));
        Assert.Equal("{corrupted", File.ReadAllText(BootstrapPath));
        Assert.False(Directory.Exists(FirstUsePath));
    }

    [Fact]
    public void OversizedPortableFileCannotFallBackToValidInstalledRoot()
    {
        PrepareInstalled(Id);
        var portablePath = Path.Combine(AppDirectory, "smartsql.portable.json");
        Directory.CreateDirectory(AppDirectory);
        var invalidFile = Enumerable.Repeat((byte)'x', StorageManifestReader.MaximumManifestBytes + 1).ToArray();
        File.WriteAllBytes(portablePath, invalidFile);

        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootReadOnlyPreflight.Inspect(Input()));
        Assert.Equal(invalidFile, File.ReadAllBytes(portablePath));
        Assert.True(File.Exists(BootstrapPath));
        Assert.False(Directory.Exists(FirstUsePath));
    }

    [Fact]
    public void MissingBootstrapAndUnconfirmedLegacyScanCannotStartFresh()
    {
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootReadOnlyPreflight.Inspect(Input()));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void ConfirmedKnownLegacyStateRequiresSelectionRatherThanNewStorage()
    {
        var oldData = Path.Combine(_root, "run-data", "local");
        var selection = StorageRootReadOnlyPreflight.Inspect(
            Input(legacy: new[] { oldData }, legacyCompleted: true));
        Assert.Equal(StorageSelectionKind.LegacySelectionRequired, selection.Kind);
        Assert.Null(selection.DataDirectory);
        Assert.Equal(new[] { Path.GetFullPath(oldData) }, selection.LegacyCandidates);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void ConfirmedNoLegacyStateReturnsOnlyANewCandidate()
    {
        var selection = StorageRootReadOnlyPreflight.Inspect(Input(legacyCompleted: true));
        Assert.Equal(StorageSelectionKind.FirstUseCandidate, selection.Kind);
        Assert.Equal(Path.GetFullPath(FirstUsePath), selection.DataDirectory);
        Assert.False(Directory.Exists(_root));
    }

    private static string TestRoot()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTSQL_TEST_DIRECTORY");
        var root = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(SourceRoot(), ".workspace", "test-temp");
        if (OperatingSystem.IsWindows() &&
            !string.Equals(Path.GetPathRoot(root), @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SmartSQL synthetic tests must remain on D:.");
        return root;
    }

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SmartSQL.sln")))
                return directory.FullName;
        throw new InvalidOperationException("Storage preflight tests require the SmartSQL source workspace.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
