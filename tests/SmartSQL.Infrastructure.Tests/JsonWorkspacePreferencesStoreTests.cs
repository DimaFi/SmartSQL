using SmartSQL.Application;

namespace SmartSQL.Infrastructure.Tests;

public sealed class JsonWorkspacePreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(TestPaths.Root, "workspace-preferences", Guid.NewGuid().ToString("N"));

    [Xunit.Fact]
    public async Task MissingPreferencesReturnNoChoiceWithoutCreatingFiles()
    {
        var store = Store();
        Xunit.Assert.Null(await store.LoadModeAsync());
        Xunit.Assert.False(Directory.Exists(_directory));
    }

    [Xunit.Theory]
    [Xunit.InlineData(WorkspacePresentationMode.Simple)]
    [Xunit.InlineData(WorkspacePresentationMode.Advanced)]
    public async Task BothModesRoundTripAcrossStoreInstances(WorkspacePresentationMode mode)
    {
        var store = Store();
        await store.SaveModeAsync(mode);
        Xunit.Assert.Equal(mode, await Store().LoadModeAsync());
        Xunit.Assert.Equal($"{{\"format\":\"SmartSQL.WorkspacePreferences.v1\",\"mode\":\"{mode}\"}}",
            await File.ReadAllTextAsync(store.FilePath));
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Fact]
    public async Task SwitchingModeReplacesTheStoredChoice()
    {
        var store = Store();
        await store.SaveModeAsync(WorkspacePresentationMode.Simple);
        await store.SaveModeAsync(WorkspacePresentationMode.Advanced);
        Xunit.Assert.Equal(WorkspacePresentationMode.Advanced, await store.LoadModeAsync());
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Theory]
    [Xunit.InlineData("")]
    [Xunit.InlineData("{ invalid JSON")]
    [Xunit.InlineData("null")]
    [Xunit.InlineData("[]")]
    [Xunit.InlineData("{}")]
    [Xunit.InlineData("{\"format\":\"future-version\",\"mode\":\"Simple\"}")]
    [Xunit.InlineData("{\"format\":\"SmartSQL.WorkspacePreferences.v1\",\"mode\":\"simple\"}")]
    [Xunit.InlineData("{\"format\":\"SmartSQL.WorkspacePreferences.v1\",\"mode\":\"0\"}")]
    [Xunit.InlineData("{\"format\":\"SmartSQL.WorkspacePreferences.v1\",\"mode\":0}")]
    [Xunit.InlineData("{\"format\":\"SmartSQL.WorkspacePreferences.v1\",\"mode\":null}")]
    [Xunit.InlineData("{\"format\":false,\"mode\":\"Simple\"}")]
    [Xunit.InlineData("{\"format\":\"SmartSQL.WorkspacePreferences.v1\",\"mode\":\"Simple\",\"mode\":\"Advanced\"}")]
    [Xunit.InlineData("{\"format\":\"SmartSQL.WorkspacePreferences.v1\",\"mode\":\"Simple\",\"writeEnabled\":true}")]
    public async Task DamagedOrUnsupportedSettingsAreReportedAndPreserved(string content)
    {
        var store = Store();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(store.FilePath, content);
        var before = await File.ReadAllBytesAsync(store.FilePath);
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadModeAsync());
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.SaveModeAsync(WorkspacePresentationMode.Advanced));
        Xunit.Assert.Equal(before, await File.ReadAllBytesAsync(store.FilePath));
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Fact]
    public async Task InvalidAndCancelledSavesPreserveThePreviousChoice()
    {
        var store = Store();
        await store.SaveModeAsync(WorkspacePresentationMode.Simple);
        var before = await File.ReadAllBytesAsync(store.FilePath);
        await Xunit.Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveModeAsync((WorkspacePresentationMode)99));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveModeAsync(WorkspacePresentationMode.Advanced, cancellation.Token));
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadModeAsync(cancellation.Token));
        Xunit.Assert.Equal(before, await File.ReadAllBytesAsync(store.FilePath));
        Xunit.Assert.Equal(WorkspacePresentationMode.Simple, await store.LoadModeAsync());
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Fact]
    public async Task CancelledFirstSaveDoesNotCreatePreferences()
    {
        var store = Store();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveModeAsync(WorkspacePresentationMode.Simple, cancellation.Token));
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadModeAsync(cancellation.Token));
        Xunit.Assert.False(Directory.Exists(_directory));
    }

    [Xunit.Fact]
    public async Task AtomicPublicationFailurePreservesThePreviouslyStoredChoice()
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows sharing can deliberately block the publication rename.
        var store = Store();
        await store.SaveModeAsync(WorkspacePresentationMode.Simple);
        var before = await File.ReadAllBytesAsync(store.FilePath);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Xunit.Record.ExceptionAsync(() => store.SaveModeAsync(WorkspacePresentationMode.Advanced));
            Xunit.Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Expected an I/O or access failure, received {failure?.GetType().Name ?? "no failure"}.");
        }
        Xunit.Assert.Equal(before, await File.ReadAllBytesAsync(store.FilePath));
        Xunit.Assert.Equal(WorkspacePresentationMode.Simple, await store.LoadModeAsync());
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Fact]
    public async Task PreferencesAreBoundedAndRequireAnExplicitAbsolutePath()
    {
        Xunit.Assert.Throws<ArgumentException>(() => new JsonWorkspacePreferencesStore("preferences.json"));
        Xunit.Assert.Throws<ArgumentException>(() => new JsonWorkspacePreferencesStore(_directory + Path.DirectorySeparatorChar));
        var store = Store();
        Xunit.Assert.Equal(Path.Combine(_directory, "preferences.json"), store.FilePath);
        Directory.CreateDirectory(_directory);
        var before = new byte[JsonWorkspacePreferencesStore.MaxFileBytes + 1];
        await File.WriteAllBytesAsync(store.FilePath, before);
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadModeAsync());
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.SaveModeAsync(WorkspacePresentationMode.Simple));
        Xunit.Assert.Equal(before, await File.ReadAllBytesAsync(store.FilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private JsonWorkspacePreferencesStore Store() => new(Path.Combine(_directory, "preferences.json"));
}
