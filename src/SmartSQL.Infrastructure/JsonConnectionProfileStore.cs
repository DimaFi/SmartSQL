using System.Text.Json;
using SmartSQL.Application;

namespace SmartSQL.Infrastructure;

/// <summary>Bounded, strictly validated nonsecret profiles with atomic same-directory publication.</summary>
public sealed class JsonConnectionProfileStore : IConnectionProfileStore
{
    public const int MaxProfiles = 100;
    public const int MaxFileBytes = 1_048_576;
    private const string Format = "SmartSQL.ConnectionProfiles.v1";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConnectionProfileStore(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
            throw new ArgumentException("Путь к профилям подключений должен быть абсолютным.", nameof(absoluteFilePath));
        FilePath = Path.GetFullPath(absoluteFilePath);
        if (string.IsNullOrEmpty(Path.GetFileName(FilePath)))
            throw new ArgumentException("Укажите путь к файлу профилей подключений.", nameof(absoluteFilePath));
    }

    public string FilePath { get; }

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FileStream stream;
            try
            {
                stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous);
            }
            catch (FileNotFoundException)
            {
                return Array.Empty<ConnectionProfile>();
            }
            catch (DirectoryNotFoundException)
            {
                return Array.Empty<ConnectionProfile>();
            }
            await using (stream)
            {
                if (stream.Length > MaxFileBytes)
                    throw new FormatException("Файл профилей подключений превышает предел 1 МБ.");
                using var content = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (content.Length + count > MaxFileBytes)
                        throw new FormatException("Файл профилей подключений превышает предел 1 МБ.");
                    content.Write(buffer, 0, count);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return ReadProfiles(content.ToArray());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count > MaxProfiles)
            throw new ArgumentException($"Можно сохранить не более {MaxProfiles} подключений.", nameof(profiles));
        var snapshot = profiles.ToArray();
        ValidateProfiles(snapshot);
        var bytes = WriteProfiles(snapshot);
        if (bytes.Length > MaxFileBytes)
            throw new ArgumentException("Профили подключений превышают предел 1 МБ.", nameof(profiles));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = Path.Combine(Path.GetDirectoryName(FilePath)!, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous))
            {
                ownsTemporary = true;
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (ownsTemporary)
                    File.Delete(temporary);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static void ValidateProfiles(IReadOnlyList<ConnectionProfile> profiles)
    {
        if (profiles.Count > MaxProfiles)
            throw new ArgumentException($"Можно сохранить не более {MaxProfiles} подключений.", nameof(profiles));
        foreach (var profile in profiles)
        {
            if (profile is null)
                throw new ArgumentException("Профиль подключения не может быть пустым.", nameof(profiles));
            profile.Validate();
        }
        if (profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count)
            throw new ArgumentException("Идентификаторы профилей подключений не должны повторяться.", nameof(profiles));
    }

    private static byte[] WriteProfiles(IReadOnlyList<ConnectionProfile> profiles)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteStartArray("profiles");
            foreach (var profile in profiles)
            {
                writer.WriteStartObject();
                writer.WriteString("id", profile.Id);
                writer.WriteString("name", profile.Name);
                writer.WriteString("provider", profile.Provider);
                writer.WriteString("host", profile.Host);
                writer.WriteNumber("port", profile.Port);
                writer.WriteString("database", profile.Database);
                writer.WriteString("username", profile.Username);
                writer.WriteString("sslPolicy", profile.SslPolicy.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return output.ToArray();
    }

    private static IReadOnlyList<ConnectionProfile> ReadProfiles(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            ValidateProperties(root, ["format", "profiles"]);
            if (ReadString(root, "format") != Format)
                throw new FormatException("Неподдерживаемая версия файла профилей подключений.");
            var items = root.GetProperty("profiles");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaxProfiles)
                throw new FormatException("Неверный список профилей подключений или превышен предел 100 подключений.");
            var profiles = new List<ConnectionProfile>(items.GetArrayLength());
            foreach (var item in items.EnumerateArray())
            {
                ValidateProperties(item, ["id", "name", "provider", "host", "port", "database", "username", "sslPolicy"]);
                if (!Guid.TryParse(ReadString(item, "id"), out var id))
                    throw new FormatException("Неверный идентификатор профиля подключения.");
                var portElement = item.GetProperty("port");
                if (portElement.ValueKind != JsonValueKind.Number || !portElement.TryGetInt32(out var port))
                    throw new FormatException("Неверный порт профиля подключения.");
                var sslText = ReadString(item, "sslPolicy");
                if (!Enum.TryParse<ConnectionSslPolicy>(sslText, out var ssl) || !Enum.IsDefined(ssl) || ssl.ToString() != sslText)
                    throw new FormatException("Неверная политика SSL профиля подключения.");
                profiles.Add(new ConnectionProfile(id, ReadString(item, "name"), ReadString(item, "provider"),
                    ReadString(item, "host"), port, ReadString(item, "database"), ReadString(item, "username"), ssl));
            }
            ValidateProfiles(profiles);
            return Array.AsReadOnly(profiles.ToArray());
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Файл профилей подключений повреждён или содержит недопустимые настройки.", exception);
        }
    }

    private static void ValidateProperties(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException("Неверная структура файла профилей подключений.");
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != expected.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || actual.Any(name => !expected.Contains(name, StringComparer.Ordinal)))
            throw new FormatException("Файл профилей подключений содержит отсутствующие, неизвестные или повторяющиеся свойства. Секреты и строки подключения не сохраняются.");
    }

    private static string ReadString(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        return property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new FormatException("Неверный тип свойства профиля подключения.");
    }
}
