using System.Text;
using System.Text.Json;

namespace SmartSQL.Desktop.Storage;

/// <summary>
/// Side-effect-free candidate selection for M01.04/D056. This is NOT a filesystem validator,
/// bootstrap writer, legacy migrator, or active application resolver. Callers must check
/// real filesystem identity and ownership before opening preferences or a database.
/// </summary>
public static class StorageRootSelectionPolicy
{
    private const int MaximumMarkerBytes = 4096;

    public static StorageRootSelection Select(StorageRootSelectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Presence is intentionally separate from the value: an empty override must fail.
        if (input.OverridePresent)
            return new(StorageSelectionKind.ExplicitOverride,
                RequireAbsoluteDirectory(input.OverrideValue, "SMARTSQL_DATA_DIRECTORY"), null, Array.Empty<string>());

        var executableDirectory = RequireAbsoluteDirectory(input.ExecutableDirectory, "ExecutableDirectory");
        if (input.PortableMarkerPresent)
        {
            var properties = ReadMarker(input.PortableMarkerJson, "smartsql.portable.json",
                "schemaVersion", "dataDirectory");
            RequireVersionOne(properties, "smartsql.portable.json");
            var configured = RequireString(properties, "dataDirectory", "smartsql.portable.json");
            string directory;
            try
            {
                directory = Path.IsPathFullyQualified(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(configured, executableDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new StorageRootSelectionException("Некорректный путь portable-хранилища.", exception);
            }

            return new(StorageSelectionKind.Portable, RequireSafeLexicalDirectory(directory, "Portable dataDirectory"),
                null, Array.Empty<string>());
        }

        if (input.InstalledBootstrapPresent)
        {
            var properties = ReadMarker(input.InstalledBootstrapJson, "storage.json",
                "schemaVersion", "dataDirectory", "rootId");
            RequireVersionOne(properties, "storage.json");
            var configured = RequireString(properties, "dataDirectory", "storage.json");
            var rootId = RequireString(properties, "rootId", "storage.json");
            if (!Guid.TryParseExact(rootId, "D", out var id) || id == Guid.Empty)
                throw new StorageRootSelectionException("Некорректный rootId в storage.json.");
            return new(StorageSelectionKind.InstalledExisting,
                RequireAbsoluteDirectory(configured, "storage.json dataDirectory"), id, Array.Empty<string>());
        }

        var legacy = DistinctLegacyPaths(input.LegacyRootsWithData);
        if (legacy.Count > 0)
            return new(StorageSelectionKind.LegacySelectionRequired, null, null, legacy);

        // Only a *candidate*. The IO stage must prove the target is empty and no old
        // state exists before creating either root marker or installed bootstrap.
        return new(StorageSelectionKind.FirstUseCandidate,
            RequireAbsoluteDirectory(input.InstalledDefaultDataDirectory, "InstalledDefaultDataDirectory"),
            null, Array.Empty<string>());
    }

    /// <summary>
    /// Parses a root identity marker without touching disk. Caller separately verifies
    /// that its GUID matches the installed bootstrap's GUID.
    /// </summary>
    public static Guid ParseRootIdentity(string? rootMarkerJson)
    {
        var properties = ReadMarker(rootMarkerJson, "storage-root.json", "schemaVersion", "rootId");
        RequireVersionOne(properties, "storage-root.json");
        var rootId = RequireString(properties, "rootId", "storage-root.json");
        if (!Guid.TryParseExact(rootId, "D", out var id) || id == Guid.Empty)
            throw new StorageRootSelectionException("Некорректный rootId в storage-root.json.");
        return id;
    }

    private static IReadOnlyList<string> DistinctLegacyPaths(IReadOnlyList<string>? candidatePaths)
    {
        if (candidatePaths is null || candidatePaths.Count == 0)
            return Array.Empty<string>();

        // Paths were already identified as containing state by a bounded *external* probe.
        // Here we do not examine directories, choose a winner or migrate any data.
        var result = new List<string>();
        var unique = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (candidatePaths.Count > 32)
            throw new StorageRootSelectionException("Слишком много известных legacy-кандидатов.");
        foreach (var candidate in candidatePaths)
        {
            var normalized = RequireAbsoluteDirectory(candidate, "Legacy directory");
            if (unique.Add(normalized))
                result.Add(normalized);
        }

        return result.AsReadOnly();
    }

    private static string RequireAbsoluteDirectory(string? path, string purpose)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new StorageRootSelectionException($"{purpose}: требуется непустой абсолютный путь.");
        try
        {
            if (!Path.IsPathFullyQualified(path))
                throw new StorageRootSelectionException($"{purpose}: требуется абсолютный путь.");
            return RequireSafeLexicalDirectory(Path.GetFullPath(path), purpose);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new StorageRootSelectionException($"{purpose}: некорректный путь.", exception);
        }
    }

    private static string RequireSafeLexicalDirectory(string directory, string purpose)
    {
        // This only rules out obviously dangerous names. Reparse points, network mounts,
        // app/version ownership and path substitution require filesystem/handle checks.
        if (string.Equals(Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(directory)!), 
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new StorageRootSelectionException($"{purpose}: корень тома не может быть хранилищем.");
        if (OperatingSystem.IsWindows() &&
            (directory.StartsWith(@"\\", StringComparison.Ordinal) ||
             directory.StartsWith(@"//", StringComparison.Ordinal)))
            throw new StorageRootSelectionException($"{purpose}: сетевой UNC-путь не поддерживается.");
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-journal", StringComparison.OrdinalIgnoreCase))
            throw new StorageRootSelectionException($"{purpose}: файл базы или sidecar не является каталогом.");
        return directory;
    }

    private static Dictionary<string, JsonElement> ReadMarker(
        string? json, string filename, params string[] allowedFields)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumMarkerBytes)
            throw new StorageRootSelectionException($"{filename}: файл отсутствует или слишком велик.");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new StorageRootSelectionException($"{filename}: ожидается объект JSON.");
            var allowed = new HashSet<string>(allowedFields, StringComparer.Ordinal);
            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!allowed.Contains(property.Name) || !properties.TryAdd(property.Name, property.Value.Clone()))
                    throw new StorageRootSelectionException($"{filename}: неизвестное или повторное поле.");
            }
            if (properties.Count != allowed.Count)
                throw new StorageRootSelectionException($"{filename}: отсутствует обязательное поле.");
            return properties;
        }
        catch (JsonException exception)
        {
            throw new StorageRootSelectionException($"{filename}: повреждённый JSON.", exception);
        }
    }

    private static void RequireVersionOne(Dictionary<string, JsonElement> marker, string filename)
    {
        var value = marker["schemaVersion"];
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var version) || version != 1)
            throw new StorageRootSelectionException($"{filename}: версия схемы не поддерживается.");
    }

    private static string RequireString(Dictionary<string, JsonElement> marker, string property, string filename)
    {
        if (marker[property].ValueKind != JsonValueKind.String)
            throw new StorageRootSelectionException($"{filename}: поле {property} должно быть строкой.");
        var value = marker[property].GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new StorageRootSelectionException($"{filename}: поле {property} пустое.");
        return value;
    }
}

public enum StorageSelectionKind
{
    ExplicitOverride,
    Portable,
    InstalledExisting,
    LegacySelectionRequired,
    FirstUseCandidate
}

public sealed record StorageRootSelectionInput(
    string ExecutableDirectory,
    string InstalledDefaultDataDirectory,
    bool OverridePresent = false,
    string? OverrideValue = null,
    bool PortableMarkerPresent = false,
    string? PortableMarkerJson = null,
    bool InstalledBootstrapPresent = false,
    string? InstalledBootstrapJson = null,
    IReadOnlyList<string>? LegacyRootsWithData = null);

public sealed record StorageRootSelection(
    StorageSelectionKind Kind,
    string? DataDirectory,
    Guid? ExpectedRootId,
    IReadOnlyList<string> LegacyCandidates);

public sealed class StorageRootSelectionException : Exception
{
    public StorageRootSelectionException(string message) : base(message) { }
    public StorageRootSelectionException(string message, Exception innerException)
        : base(message, innerException) { }
}
