using System.Text;
using SmartSQL.Desktop.Storage;
using Xunit;

namespace SmartSQL.Desktop.Tests;

/// <summary>
/// Synthetic files only. On Windows the test root is explicitly required on D:,
/// following AGENTS.md. The reader itself never writes user files.
/// </summary>
public sealed class StorageManifestReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(TestRoot(), "storage-manifest-reader", Guid.NewGuid().ToString("N"));

    private string PathFor(StorageManifestKind kind) => Path.Combine(_root, kind switch
    {
        StorageManifestKind.Portable => "smartsql.portable.json",
        StorageManifestKind.InstalledBootstrap => "storage.json",
        StorageManifestKind.RootIdentity => "storage-root.json",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });

    private void Write(StorageManifestKind kind, byte[] bytes)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(PathFor(kind), bytes);
    }

    [Theory]
    [InlineData(StorageManifestKind.Portable)]
    [InlineData(StorageManifestKind.InstalledBootstrap)]
    [InlineData(StorageManifestKind.RootIdentity)]
    public void MissingOptionalManifestIsDifferentFromRequiredManifest(StorageManifestKind kind)
    {
        var path = PathFor(kind);
        var optional = StorageManifestReader.ReadOptional(path, kind);
        Assert.False(optional.IsPresent);
        Assert.Null(optional.Json);
        Assert.False(Directory.Exists(_root));
        Assert.Throws<StorageRootSelectionException>(() => StorageManifestReader.ReadRequired(path, kind));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void AValidInstalledBootstrapIsReadWithoutChangingItsBytes()
    {
        var json = """{"schemaVersion":1,"dataDirectory":"D:\\SmartSQL\\data","rootId":"ebca7b93-09a8-49b7-b798-092fe674298c"}""";
        var bytes = Encoding.UTF8.GetBytes(json);
        Write(StorageManifestKind.InstalledBootstrap, bytes);
        var actual = StorageManifestReader.ReadRequired(
            PathFor(StorageManifestKind.InstalledBootstrap), StorageManifestKind.InstalledBootstrap);
        Assert.Equal(json, actual);
        Assert.Equal(bytes, File.ReadAllBytes(PathFor(StorageManifestKind.InstalledBootstrap)));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void ReaderAndSelectorCanValidateMatchingRootIdentityWithoutWriting()
    {
        const string marker = """{"schemaVersion":1,"rootId":"ebca7b93-09a8-49b7-b798-092fe674298c"}""";
        Write(StorageManifestKind.RootIdentity, Encoding.UTF8.GetBytes(marker));
        var json = StorageManifestReader.ReadRequired(
            PathFor(StorageManifestKind.RootIdentity), StorageManifestKind.RootIdentity);
        Assert.Equal(Guid.Parse("ebca7b93-09a8-49b7-b798-092fe674298c"),
            StorageRootSelectionPolicy.ParseRootIdentity(json));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void EmptyManifestNeverAppearsAbsent()
    {
        Write(StorageManifestKind.Portable, Array.Empty<byte>());
        Assert.Throws<StorageRootSelectionException>(() => StorageManifestReader.ReadOptional(
            PathFor(StorageManifestKind.Portable), StorageManifestKind.Portable));
        Assert.True(File.Exists(PathFor(StorageManifestKind.Portable)));
    }

    [Theory]
    [InlineData(4097)]
    [InlineData(16000)]
    public void OversizedFileIsRejectedWithoutChangingOrTruncatingIt(int size)
    {
        var bytes = Enumerable.Repeat((byte)' ', size).ToArray();
        Write(StorageManifestKind.InstalledBootstrap, bytes);
        Assert.Throws<StorageRootSelectionException>(() => StorageManifestReader.ReadOptional(
            PathFor(StorageManifestKind.InstalledBootstrap), StorageManifestKind.InstalledBootstrap));
        Assert.Equal(bytes, File.ReadAllBytes(PathFor(StorageManifestKind.InstalledBootstrap)));
    }

    [Fact]
    public void ExactlyMaximumUtf8BytesCanBeRead()
    {
        var content = "{}" + new string(' ', StorageManifestReader.MaximumManifestBytes - 2);
        Write(StorageManifestKind.Portable, Encoding.UTF8.GetBytes(content));
        Assert.Equal(content, StorageManifestReader.ReadRequired(
            PathFor(StorageManifestKind.Portable), StorageManifestKind.Portable));
    }

    [Fact]
    public void Utf8ByteLimitIsNotACharacterLimit()
    {
        // Cyrillic characters take two UTF-8 bytes. A character-count guard would
        // incorrectly accept this file, even though it is over the 4 KB limit.
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            dataDirectory = new string('Ж', StorageManifestReader.MaximumManifestBytes / 2)
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.True(bytes.Length > StorageManifestReader.MaximumManifestBytes);
        Write(StorageManifestKind.Portable, bytes);
        Assert.Throws<StorageRootSelectionException>(() => StorageManifestReader.ReadOptional(
            PathFor(StorageManifestKind.Portable), StorageManifestKind.Portable));
        Assert.Equal(bytes, File.ReadAllBytes(PathFor(StorageManifestKind.Portable)));
    }

    [Fact]
    public void ExistingMalformedJsonMustBePassedToStrictPolicyInsteadOfFallingBack()
    {
        const string damaged = """{"schemaVersion":1,"rootId":"oops"}""";
        Write(StorageManifestKind.RootIdentity, Encoding.UTF8.GetBytes(damaged));
        var read = StorageManifestReader.ReadOptional(
            PathFor(StorageManifestKind.RootIdentity), StorageManifestKind.RootIdentity);
        Assert.True(read.IsPresent);
        Assert.Equal(damaged, read.Json);
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageRootSelectionPolicy.ParseRootIdentity(read.Json));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void InvalidUtf8AndBomAreExplicitFailuresAndSourceIsUnchanged()
    {
        var path = PathFor(StorageManifestKind.Portable);
        foreach (var bytes in new[]
        {
            new byte[] { 0x7B, 0xFF, 0x7D },
            new byte[] { 0xEF, 0xBB, 0xBF, 0x7B, 0x7D }
        })
        {
            Write(StorageManifestKind.Portable, bytes);
            Assert.Throws<StorageRootSelectionException>(() =>
                StorageManifestReader.ReadOptional(path, StorageManifestKind.Portable));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData("unknown.json")]
    [InlineData("storage-root.json")]
    [InlineData("smartsql.portable.json")]
    public void InstalledBootstrapCannotReadAnyOtherFilename(string wrongName)
    {
        var wrongPath = Path.Combine(_root, wrongName);
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageManifestReader.ReadOptional(wrongPath, StorageManifestKind.InstalledBootstrap));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void RelativeOrRootOnlyPathIsRejectedWithoutAnyFileSystemActivity()
    {
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageManifestReader.ReadOptional("storage.json", StorageManifestKind.InstalledBootstrap));
        Assert.Throws<StorageRootSelectionException>(() =>
            StorageManifestReader.ReadOptional(Path.GetPathRoot(_root)!, StorageManifestKind.InstalledBootstrap));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(@"\\smartsql-invalid\share\storage.json")]
    [InlineData(@"\\?\D:\smartsql-invalid\storage.json")]
    [InlineData(@"\\.\D:\smartsql-invalid\storage.json")]
    [InlineData("//smartsql-invalid/share/storage.json")]
    public void WindowsNetworkOrDeviceManifestPathIsRejectedBeforeOpen(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var error = Assert.Throws<StorageRootSelectionException>(() =>
            StorageManifestReader.ReadOptional(path, StorageManifestKind.InstalledBootstrap));

        Assert.Contains("UNC/device", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
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
        throw new InvalidOperationException("Storage manifest tests require the SmartSQL source workspace.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
