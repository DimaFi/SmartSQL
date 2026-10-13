using System.Text.Json.Nodes;
using SmartSQL.Application;

namespace SmartSQL.Infrastructure.Tests;

public sealed class JsonConnectionProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(TestPaths.Root, "connection-profiles", Guid.NewGuid().ToString("N"));

    [Xunit.Fact]
    public async Task RoundTripPreservesOnlyTheStructuredNonsecretProfileFields()
    {
        var store = Store();
        var first = Profile("База 🌍");
        var second = Profile("Другой провайдер") with { Provider = "future-provider", Host = "::1", SslPolicy = ConnectionSslPolicy.Require };
        Xunit.Assert.Empty(await store.LoadAsync());
        await store.SaveAsync([first, second]);
        var actual = await store.LoadAsync();
        Xunit.Assert.Equal([first, second], actual);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.FilePath))!.AsObject();
        Xunit.Assert.Equal("SmartSQL.ConnectionProfiles.v1", (string?)json["format"]);
        var serialized = json["profiles"]![0]!.AsObject();
        Xunit.Assert.Equal(["id", "name", "provider", "host", "port", "database", "username", "sslPolicy"], serialized.Select(item => item.Key));
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Fact]
    public async Task InvalidOrCancelledSavePreservesThePreviouslyPublishedFile()
    {
        var store = Store();
        var original = Profile("Сохранённое подключение");
        await store.SaveAsync([original]);
        var before = await File.ReadAllBytesAsync(store.FilePath);
        await Xunit.Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync([original with { Port = 70000 }]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync([Profile("Не должно сохраниться")], cancellation.Token));
        Xunit.Assert.Equal(before, await File.ReadAllBytesAsync(store.FilePath));
        Xunit.Assert.Equal(original, Xunit.Assert.Single(await store.LoadAsync()));
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Fact]
    public async Task AtomicReplacementFailureDoesNotDeleteThePreviousProfiles()
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows file sharing can deliberately block the publication rename.
        var store = Store();
        var original = Profile("Предыдущая версия");
        await store.SaveAsync([original]);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Xunit.Record.ExceptionAsync(() => store.SaveAsync([Profile("Новая версия")]));
            Xunit.Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Expected an I/O or access failure, received {failure?.GetType().Name ?? "no failure"}.");
        }
        Xunit.Assert.Equal(original, Xunit.Assert.Single(await store.LoadAsync()));
        Xunit.Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Xunit.Theory]
    [Xunit.InlineData("password")]
    [Xunit.InlineData("connectionString")]
    [Xunit.InlineData("uri")]
    public async Task UnknownCredentialOrConnectionStringPropertiesAreRejected(string property)
    {
        var store = Store();
        await store.SaveAsync([Profile("Корректный профиль")]);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.FilePath))!;
        json["profiles"]![0]![property] = "sentinel-do-not-log";
        await File.WriteAllTextAsync(store.FilePath, json.ToJsonString());
        var error = await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadAsync());
        Xunit.Assert.DoesNotContain("sentinel-do-not-log", error.Message);
    }

    [Xunit.Fact]
    public async Task DuplicateIdsAndMalformedSettingsCannotBeSilentlyDiscarded()
    {
        var store = Store();
        var profile = Profile("Один");
        await Xunit.Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync([profile, profile with { Name = "Два" }]));
        await store.SaveAsync([profile]);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.FilePath))!;
        json["profiles"]!.AsArray().Add(json["profiles"]![0]!.DeepClone());
        await File.WriteAllTextAsync(store.FilePath, json.ToJsonString());
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadAsync());
        await File.WriteAllTextAsync(store.FilePath, "{\"format\":\"SmartSQL.ConnectionProfiles.v1\",\"format\":\"SmartSQL.ConnectionProfiles.v1\",\"profiles\":[]}");
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadAsync());
        await File.WriteAllTextAsync(store.FilePath, "{ invalid JSON");
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadAsync());
    }

    [Xunit.Fact]
    public async Task PersistenceIsBoundedAndAPathOnAnotherDriveIsNeverChosenImplicitly()
    {
        Xunit.Assert.Throws<ArgumentException>(() => new JsonConnectionProfileStore("profiles.json"));
        var store = Store();
        var profiles = Enumerable.Range(0, JsonConnectionProfileStore.MaxProfiles + 1).Select(index => Profile(index.ToString())).ToArray();
        await Xunit.Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profiles));
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(store.FilePath, new byte[JsonConnectionProfileStore.MaxFileBytes + 1]);
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadAsync());
        Xunit.Assert.Equal(Path.Combine(_directory, "profiles.json"), store.FilePath);
    }

    [Xunit.Theory]
    [Xunit.InlineData("format", "future-version")]
    [Xunit.InlineData("sslPolicy", "999")]
    [Xunit.InlineData("sslPolicy", "verifyfull")]
    [Xunit.InlineData("host", "postgresql://reader:secret@localhost/db")]
    public async Task UnsupportedVersionAndInvalidProfileValuesFailExplicitly(string property, string value)
    {
        var store = Store();
        await store.SaveAsync([Profile("Проверка")]);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.FilePath))!;
        if (property == "format")
            json[property] = value;
        else
            json["profiles"]![0]![property] = value;
        await File.WriteAllTextAsync(store.FilePath, json.ToJsonString());
        await Xunit.Assert.ThrowsAsync<FormatException>(() => store.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private JsonConnectionProfileStore Store() => new(Path.Combine(_directory, "profiles.json"));
    private static ConnectionProfile Profile(string name) => new(Guid.NewGuid(), name, DatabaseProviders.PostgreSql,
        "localhost", 5432, "smartsql", "reader", ConnectionSslPolicy.VerifyFull);
}
