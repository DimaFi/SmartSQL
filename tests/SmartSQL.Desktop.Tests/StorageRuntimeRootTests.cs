using System.Text;
using System.Text.Json;
using SmartSQL.Desktop.Storage;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class StorageRuntimeRootTests : IDisposable
{
    private readonly string _root = Path.Combine(TestRoot(), "storage-runtime", Guid.NewGuid().ToString("N"));
    private string AppDirectory => Path.Combine(_root, "product");
    private string InstalledBase => Path.Combine(_root, "installed");
    private string InstalledData => Path.Combine(InstalledBase, "data");
    private string BootstrapPath => Path.Combine(InstalledBase, "storage.json");

    private StorageRuntimeRequest Request() =>
        new(AppDirectory, InstalledBase, DevelopmentMode: true);

    private void PrepareApp() => Directory.CreateDirectory(AppDirectory);

    [Fact]
    public void FreshInstalledRootReopensWithoutChangingIdentityBytes()
    {
        PrepareApp();
        using (var lease = StorageRuntimeRoot.Open(Request()))
        {
            Assert.Equal(StorageSelectionKind.FirstUseCandidate, lease.Kind);
            Assert.Equal(Path.GetFullPath(InstalledData), lease.DataDirectory);
            Assert.True(Directory.Exists(lease.TemporaryDirectory));
            Assert.True(Directory.Exists(Path.Combine(InstalledData, "cache")));
            Assert.True(Directory.Exists(Path.Combine(InstalledData, "dotnet-bundles")));
        }
        var markerPath = Path.Combine(InstalledData, "storage-root.json");
        var markerBytes = File.ReadAllBytes(markerPath);
        var bootstrapBytes = File.ReadAllBytes(BootstrapPath);

        using var reopened = StorageRuntimeRoot.Open(Request());

        Assert.Equal(StorageSelectionKind.InstalledExisting, reopened.Kind);
        Assert.Equal(markerBytes, File.ReadAllBytes(markerPath));
        Assert.Equal(bootstrapBytes, File.ReadAllBytes(BootstrapPath));
    }

    [Fact]
    public void ExplicitRootIsProcessOnlyAndPortableRootIsStable()
    {
        PrepareApp();
        var explicitRoot = Path.Combine(_root, "explicit");
        var explicitRequest = new StorageRuntimeRequest(AppDirectory, InstalledBase,
            OverridePresent: true, OverrideValue: explicitRoot, DevelopmentMode: true);
        using (var lease = StorageRuntimeRoot.Open(explicitRequest))
            Assert.Equal(StorageSelectionKind.ExplicitOverride, lease.Kind);
        var explicitMarker = File.ReadAllBytes(Path.Combine(explicitRoot, "storage-root.json"));
        Assert.False(File.Exists(BootstrapPath));
        using (StorageRuntimeRoot.Open(explicitRequest)) { }
        Assert.Equal(explicitMarker, File.ReadAllBytes(Path.Combine(explicitRoot, "storage-root.json")));

        var portableRoot = Path.Combine(_root, "portable-data");
        File.WriteAllText(Path.Combine(AppDirectory, "smartsql.portable.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            dataDirectory = portableRoot
        }), new UTF8Encoding(false));
        using var portable = StorageRuntimeRoot.Open(Request());
        Assert.Equal(StorageSelectionKind.Portable, portable.Kind);
        Assert.Equal(Path.GetFullPath(portableRoot), portable.DataDirectory);
        Assert.False(File.Exists(BootstrapPath));
    }

    [Fact]
    public void HigherPriorityOverrideAndPortableIgnoreInvalidInstalledBase()
    {
        PrepareApp();
        var invalidInstalledBase = Path.Combine(AppDirectory, "ignored-installed");
        var explicitRoot = Path.Combine(_root, "explicit-priority");
        using (var lease = StorageRuntimeRoot.Open(new StorageRuntimeRequest(
                   AppDirectory, invalidInstalledBase, true, explicitRoot, DevelopmentMode: true)))
            Assert.Equal(StorageSelectionKind.ExplicitOverride, lease.Kind);
        Assert.False(Directory.Exists(invalidInstalledBase));

        var portableRoot = Path.Combine(_root, "portable-priority");
        File.WriteAllText(Path.Combine(AppDirectory, "smartsql.portable.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            dataDirectory = portableRoot
        }));
        using var portable = StorageRuntimeRoot.Open(new StorageRuntimeRequest(
            AppDirectory, invalidInstalledBase, DevelopmentMode: true));
        Assert.Equal(StorageSelectionKind.Portable, portable.Kind);
        Assert.False(Directory.Exists(invalidInstalledBase));
    }

    [Fact]
    public void ExistingOnlyRejectsMissingAndEmptyRootsWithoutAdoption()
    {
        PrepareApp();
        var selected = Path.Combine(_root, "selected");
        var request = new StorageRuntimeRequest(AppDirectory, InstalledBase,
            OverridePresent: true, OverrideValue: selected, DevelopmentMode: true, ExistingOnly: true);

        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(request));
        Assert.False(Directory.Exists(selected));
        Directory.CreateDirectory(selected);
        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(request));
        Assert.Empty(Directory.EnumerateFileSystemEntries(selected));
        Assert.False(File.Exists(BootstrapPath));
    }

    [Fact]
    public void KnownLegacyStateBlocksFirstUseWithoutRecursiveSearch()
    {
        PrepareApp();
        var legacy = Path.Combine(AppDirectory, "data", "local");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "workspace-preferences.json"), "{}");

        var error = Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(Request()));

        Assert.Contains("миграц", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(InstalledBase));
    }

    [Fact]
    public void InterruptedInstalledInitializationLeavesOrphanThatIsNeverAdopted()
    {
        PrepareApp();
        var request = Request() with
        {
            Checkpoint = stage =>
            {
                if (stage != "root-marker-published") return;
                throw new InjectedFailureException();
            }
        };

        Assert.Throws<InjectedFailureException>(() => StorageRuntimeRoot.Open(request));
        var markerPath = Path.Combine(InstalledData, "storage-root.json");
        var markerBytes = File.ReadAllBytes(markerPath);
        Assert.False(File.Exists(BootstrapPath));

        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(Request()));
        Assert.Equal(markerBytes, File.ReadAllBytes(markerPath));
        Assert.False(File.Exists(BootstrapPath));
    }

    [Fact]
    public void UnknownNonemptyAndFutureMarkerAreRefusedWithoutChanges()
    {
        PrepareApp();
        var selected = Path.Combine(_root, "selected");
        Directory.CreateDirectory(selected);
        var unknownPath = Path.Combine(selected, "unknown.bin");
        File.WriteAllBytes(unknownPath, new byte[] { 1, 2, 3 });
        var request = new StorageRuntimeRequest(AppDirectory, InstalledBase,
            OverridePresent: true, OverrideValue: selected, DevelopmentMode: true);
        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(request));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(unknownPath));
        Assert.False(File.Exists(Path.Combine(selected, "storage-root.json")));

        File.Delete(unknownPath);
        var markerPath = Path.Combine(selected, "storage-root.json");
        File.WriteAllText(markerPath, """{"schemaVersion":2,"rootId":"aa9782e2-049b-4fa9-9ffc-d953cc56b311"}""");
        var markerBytes = File.ReadAllBytes(markerPath);
        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(request));
        Assert.Equal(markerBytes, File.ReadAllBytes(markerPath));
    }

    [Fact]
    public void InstalledMismatchAndDisappearedRootNeverRecreateOrRewrite()
    {
        PrepareApp();
        using (StorageRuntimeRoot.Open(Request())) { }
        var bootstrapBytes = File.ReadAllBytes(BootstrapPath);
        var markerPath = Path.Combine(InstalledData, "storage-root.json");
        File.WriteAllText(markerPath, """{"schemaVersion":1,"rootId":"57ec9b48-e3bd-450d-826e-4c13ba8b8fbd"}""");
        var mismatchedBytes = File.ReadAllBytes(markerPath);

        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(Request()));
        Assert.Equal(bootstrapBytes, File.ReadAllBytes(BootstrapPath));
        Assert.Equal(mismatchedBytes, File.ReadAllBytes(markerPath));

        Directory.Delete(InstalledData, recursive: true);
        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(Request()));
        Assert.False(Directory.Exists(InstalledData));
        Assert.Equal(bootstrapBytes, File.ReadAllBytes(BootstrapPath));
    }

    [Fact]
    public void InvalidExplicitTempFailsBeforeSelectionMetadataChanges()
    {
        PrepareApp();
        using (StorageRuntimeRoot.Open(Request())) { }
        var bootstrapBytes = File.ReadAllBytes(BootstrapPath);
        var markerPath = Path.Combine(InstalledData, "storage-root.json");
        var markerBytes = File.ReadAllBytes(markerPath);
        var notDirectory = Path.Combine(_root, "temp-file");
        File.WriteAllText(notDirectory, "keep");

        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(
            Request() with { TemporaryDirectory = notDirectory }));

        Assert.Equal("keep", File.ReadAllText(notDirectory));
        Assert.Equal(bootstrapBytes, File.ReadAllBytes(BootstrapPath));
        Assert.Equal(markerBytes, File.ReadAllBytes(markerPath));
    }

    [Fact]
    public void AppContainmentAndFileComponentsAreRejectedBeforeInitialization()
    {
        PrepareApp();
        var insideApp = Path.Combine(AppDirectory, "user-data");
        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(
            new StorageRuntimeRequest(AppDirectory, InstalledBase, true, insideApp,
                DevelopmentMode: true)));
        Assert.False(Directory.Exists(insideApp));

        var component = Path.Combine(_root, "component");
        File.WriteAllText(component, "keep");
        var belowFile = Path.Combine(component, "data");
        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(
            new StorageRuntimeRequest(AppDirectory, InstalledBase, true, belowFile,
                DevelopmentMode: true)));
        Assert.Equal("keep", File.ReadAllText(component));
    }

    [Fact]
    public void SecondLeaseForSameCanonicalRootIsRefusedUntilReleased()
    {
        PrepareApp();
        var selected = Path.Combine(_root, "selected");
        var request = new StorageRuntimeRequest(AppDirectory, InstalledBase,
            OverridePresent: true, OverrideValue: selected, DevelopmentMode: true);
        using (StorageRuntimeRoot.Open(request))
            Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(request));
        using var reopened = StorageRuntimeRoot.Open(request);
        Assert.Equal(Path.GetFullPath(selected), reopened.DataDirectory);
    }

    [Fact]
    public void ExistingReparseRootIsRejectedWhenSupportedByTheHost()
    {
        if (!OperatingSystem.IsWindows())
            return;
        PrepareApp();
        var target = Path.Combine(_root, "target");
        var link = Path.Combine(_root, "linked-root");
        Directory.CreateDirectory(target);
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }
        try
        {
            Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(
                new StorageRuntimeRequest(AppDirectory, InstalledBase, true, link,
                    DevelopmentMode: true)));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    [Fact]
    public void ReparseRootMarkerIsRejectedBeforeItsContentsAreRead()
    {
        if (!OperatingSystem.IsWindows())
            return;
        PrepareApp();
        var selected = Path.Combine(_root, "selected-with-linked-marker");
        Directory.CreateDirectory(selected);
        var target = Path.Combine(_root, "marker-target.json");
        File.WriteAllText(target,
            """{"schemaVersion":1,"rootId":"fa919946-f8f4-49b1-bcf9-658808624f69"}""");
        var marker = Path.Combine(selected, "storage-root.json");
        try { File.CreateSymbolicLink(marker, target); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }

        Assert.Throws<StorageRootSelectionException>(() => StorageRuntimeRoot.Open(
            new StorageRuntimeRequest(AppDirectory, InstalledBase, true, selected,
                DevelopmentMode: true)));
        Assert.Equal(
            """{"schemaVersion":1,"rootId":"fa919946-f8f4-49b1-bcf9-658808624f69"}""",
            File.ReadAllText(target));
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
        throw new InvalidOperationException("Storage runtime tests require the SmartSQL source workspace.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class InjectedFailureException : Exception { }
}
