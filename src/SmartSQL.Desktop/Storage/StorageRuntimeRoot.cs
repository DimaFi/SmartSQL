using System.Text;
using System.Text.Json;

namespace SmartSQL.Desktop.Storage;

public sealed record StorageRuntimeRequest(
    string ExecutableDirectory,
    string InstalledBaseDirectory,
    bool OverridePresent = false,
    string? OverrideValue = null,
    string? TemporaryDirectory = null,
    bool DevelopmentMode = false,
    bool ExistingOnly = false,
    Action<string>? Checkpoint = null);

public static class StorageRuntimeRoot
{
    private const string RootMarkerName = "storage-root.json";
    private const string BootstrapName = "storage.json";

    public static StorageRuntimeLease Open(StorageRuntimeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExistingOnly && !request.OverridePresent)
            throw new StorageRootSelectionException(
                "ExistingOnly допускается только для явно выбранного process-only хранилища.");
        var executableDirectory = StoragePathGuards.RequireDirectoryPath(
            request.ExecutableDirectory, "Каталог программы", mustExist: true, request.DevelopmentMode);
        var portablePath = Path.Combine(executableDirectory, "smartsql.portable.json");
        var portablePresent = !request.OverridePresent &&
            StoragePathGuards.RequireOrdinaryFileIfPresent(portablePath, "Portable marker");
        var installedBaseDirectory = Path.Combine(executableDirectory, ".unused-installed-storage");
        if (!request.OverridePresent && !portablePresent)
        {
            installedBaseDirectory = StoragePathGuards.RequireDirectoryPath(
                request.InstalledBaseDirectory, "Каталог installed bootstrap", mustExist: false,
                request.DevelopmentMode);
        }

        string? explicitTemporaryDirectory = null;
        if (request.TemporaryDirectory is not null)
        {
            explicitTemporaryDirectory = StoragePathGuards.RequireDirectoryPath(
                request.TemporaryDirectory, "Временный каталог", mustExist: true, request.DevelopmentMode);
            StoragePathGuards.ProbeWritableDirectory(explicitTemporaryDirectory, "Временный каталог", create: false);
        }

        var selection = Select(request, executableDirectory, installedBaseDirectory);
        if (selection.Kind == StorageSelectionKind.LegacySelectionRequired)
            throw LegacyFailure(selection);

        if (selection.Kind == StorageSelectionKind.FirstUseCandidate)
        {
            var selectionMutex = StorageRuntimeLease.AcquireSelection(installedBaseDirectory);
            try
            {
                selection = Select(request, executableDirectory, installedBaseDirectory);
                if (selection.Kind == StorageSelectionKind.LegacySelectionRequired)
                    throw LegacyFailure(selection);
                return OpenSelected(request, executableDirectory, installedBaseDirectory,
                    explicitTemporaryDirectory, selection,
                    publishInstalledBootstrap: selection.Kind == StorageSelectionKind.FirstUseCandidate);
            }
            finally
            {
                StorageRuntimeLease.ReleaseSelection(selectionMutex);
            }
        }

        return OpenSelected(request, executableDirectory, installedBaseDirectory,
            explicitTemporaryDirectory, selection, publishInstalledBootstrap: false);
    }

    private static StorageRootSelection Select(StorageRuntimeRequest request,
        string executableDirectory, string installedBaseDirectory)
    {
        var portablePath = Path.Combine(executableDirectory, "smartsql.portable.json");
        if (request.OverridePresent)
        {
            var ignoredInstalledBase = Path.Combine(executableDirectory, ".unused-installed-storage");
            return StorageRootReadOnlyPreflight.Inspect(new StorageRootPreflightInput(
                executableDirectory,
                Path.Combine(ignoredInstalledBase, "data"),
                Path.Combine(ignoredInstalledBase, BootstrapName),
                OverridePresent: true,
                OverrideValue: request.OverrideValue));
        }

        var portablePresent = StoragePathGuards.RequireOrdinaryFileIfPresent(portablePath, "Portable marker");
        if (portablePresent)
        {
            var ignoredInstalledBase = Path.Combine(executableDirectory, ".unused-installed-storage");
            return StorageRootReadOnlyPreflight.Inspect(new StorageRootPreflightInput(
                executableDirectory,
                Path.Combine(ignoredInstalledBase, "data"),
                Path.Combine(ignoredInstalledBase, BootstrapName),
                OverridePresent: false,
                OverrideValue: null));
        }

        var bootstrapPath = Path.Combine(installedBaseDirectory, BootstrapName);
        var bootstrapPresent = StoragePathGuards.RequireOrdinaryFileIfPresent(
            bootstrapPath, "Installed bootstrap");

        // Validate the physical installed target before opening its root marker.
        if (bootstrapPresent)
        {
            var bootstrap = StorageManifestReader.ReadRequired(
                bootstrapPath, StorageManifestKind.InstalledBootstrap);
            var installed = StorageRootSelectionPolicy.Select(new StorageRootSelectionInput(
                executableDirectory,
                Path.Combine(installedBaseDirectory, "data"),
                InstalledBootstrapPresent: true,
                InstalledBootstrapJson: bootstrap));
            var installedRoot = StoragePathGuards.RequireDirectoryPath(
                installed.DataDirectory, "Выбранный installed root", mustExist: true, request.DevelopmentMode);
            StoragePathGuards.RequireNoAppContainment(installedRoot, executableDirectory,
                "Выбранный installed root");
            return installed with
            {
                DataDirectory = StorageRootSelectionPolicy.RequireMatchingInstalledRoot(
                    installed,
                    ReadRootMarkerAfterPhysicalValidation(installedRoot))
            };
        }

        IReadOnlyList<string>? legacy = null;
        var legacyCompleted = false;
        if (!bootstrapPresent)
        {
            legacy = FindLegacyRoots(executableDirectory, request.DevelopmentMode);
            legacyCompleted = true;
        }

        return StorageRootReadOnlyPreflight.Inspect(new StorageRootPreflightInput(
            executableDirectory,
            Path.Combine(installedBaseDirectory, "data"),
            bootstrapPath,
            request.OverridePresent,
            request.OverrideValue,
            legacy,
            legacyCompleted));
    }

    private static StorageRuntimeLease OpenSelected(StorageRuntimeRequest request,
        string executableDirectory, string installedBaseDirectory, string? explicitTemporaryDirectory,
        StorageRootSelection selection, bool publishInstalledBootstrap)
    {
        if (string.IsNullOrWhiteSpace(selection.DataDirectory))
            throw new StorageRootSelectionException("Хранилище не выбрано.");

        var dataDirectory = StoragePathGuards.RequireDirectoryPath(selection.DataDirectory, "Каталог данных",
            mustExist: selection.Kind == StorageSelectionKind.InstalledExisting, request.DevelopmentMode);
        StoragePathGuards.RequireNoAppContainment(dataDirectory, executableDirectory, "Каталог данных");

        var temporaryDirectory = StoragePathGuards.RequireDirectoryPath(
            explicitTemporaryDirectory ?? Path.Combine(dataDirectory, "temp"), "Временный каталог",
            mustExist: explicitTemporaryDirectory is not null, request.DevelopmentMode);
        var cacheDirectory = StoragePathGuards.RequireDirectoryPath(
            Path.Combine(dataDirectory, "cache"), "Каталог cache", mustExist: false, request.DevelopmentMode);
        var bundlesDirectory = StoragePathGuards.RequireDirectoryPath(
            Path.Combine(dataDirectory, "dotnet-bundles"), "Каталог dotnet-bundles", mustExist: false,
            request.DevelopmentMode);

        var acquired = StorageRuntimeLease.AcquireRoot(dataDirectory);
        try
        {
            Guid rootId;
            var markerCreated = false;
            if (selection.Kind == StorageSelectionKind.InstalledExisting)
            {
                rootId = RequireExistingMarker(dataDirectory, selection.ExpectedRootId);
            }
            else if (request.ExistingOnly)
            {
                rootId = RequireExistingMarker(dataDirectory, expectedRootId: null);
            }
            else if (publishInstalledBootstrap)
            {
                (rootId, markerCreated) = InitializeFreshInstalledRoot(dataDirectory, request.Checkpoint);
            }
            else
            {
                (rootId, markerCreated) = InitializeOrOpenExplicitRoot(dataDirectory, request.Checkpoint);
            }

            if (markerCreated && publishInstalledBootstrap)
                request.Checkpoint?.Invoke("root-marker-published");

            StoragePathGuards.ProbeWritableDirectory(dataDirectory, "Каталог данных", create: false);
            StoragePathGuards.ProbeWritableDirectory(temporaryDirectory, "Временный каталог", create: true);
            StoragePathGuards.ProbeWritableDirectory(cacheDirectory, "Каталог cache", create: true);
            StoragePathGuards.ProbeWritableDirectory(bundlesDirectory, "Каталог dotnet-bundles", create: true);

            if (publishInstalledBootstrap)
                PublishInstalledBootstrap(installedBaseDirectory, dataDirectory, rootId);

            return new StorageRuntimeLease(dataDirectory, temporaryDirectory, selection.Kind,
                acquired.Mutex, acquired.Key);
        }
        catch
        {
            acquired.Mutex.ReleaseMutex();
            acquired.Mutex.Dispose();
            StorageRuntimeLease.ReleaseFailedAcquisition(acquired.Key);
            throw;
        }
    }

    private static (Guid Id, bool Created) InitializeOrOpenExplicitRoot(
        string dataDirectory, Action<string>? checkpoint)
    {
        if (StoragePathGuards.DirectoryExists(dataDirectory, "Каталог данных") &&
            StoragePathGuards.HasAnyEntry(dataDirectory, "Каталог данных"))
            return (RequireExistingMarker(dataDirectory, expectedRootId: null), false);

        PrepareEmptyRoot(dataDirectory);
        checkpoint?.Invoke("root-prepared");
        var id = Guid.NewGuid();
        WriteNewRootMarker(dataDirectory, id);
        return (id, true);
    }

    private static (Guid Id, bool Created) InitializeFreshInstalledRoot(
        string dataDirectory, Action<string>? checkpoint)
    {
        if (StoragePathGuards.DirectoryExists(dataDirectory, "Новый installed-каталог данных") &&
            StoragePathGuards.HasAnyEntry(dataDirectory, "Новый installed-каталог данных"))
            throw new StorageRootSelectionException(
                "Новый installed root уже содержит состояние или orphan marker; автоматическое принятие запрещено.");
        PrepareEmptyRoot(dataDirectory);
        checkpoint?.Invoke("root-prepared");
        var id = Guid.NewGuid();
        WriteNewRootMarker(dataDirectory, id);
        return (id, true);
    }

    private static void PrepareEmptyRoot(string dataDirectory)
    {
        try { Directory.CreateDirectory(dataDirectory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException("Не удалось создать каталог данных.", exception);
        }

        // DeleteOnClose leaves no child directory/state if the process dies here.
        // Provision temp/cache only after the root identity exists. A later failure
        // leaves an explicit orphan marker, never a silently adopted incomplete root.
        StoragePathGuards.ProbeWritableDirectory(dataDirectory, "Каталог данных", create: false);
    }

    private static Guid RequireExistingMarker(string dataDirectory, Guid? expectedRootId)
    {
        if (!StoragePathGuards.DirectoryExists(dataDirectory, "Выбранное хранилище"))
            throw new StorageRootSelectionException(
                "Выбранное существующее хранилище отсутствует; оно не будет пересоздано.");
        var marker = ReadRootMarkerAfterPhysicalValidation(dataDirectory);
        var id = StorageRootSelectionPolicy.ParseRootIdentity(marker);
        if (expectedRootId is not null && id != expectedRootId.Value)
            throw new StorageRootSelectionException(
                "storage.json и storage-root.json указывают на разные хранилища.");
        return id;
    }

    private static string ReadRootMarkerAfterPhysicalValidation(string dataDirectory)
    {
        var markerPath = Path.Combine(dataDirectory, RootMarkerName);
        StoragePathGuards.RequireOrdinaryFileIfPresent(markerPath, "Root marker");
        return StorageManifestReader.ReadRequired(markerPath, StorageManifestKind.RootIdentity);
    }

    private static void WriteNewRootMarker(string dataDirectory, Guid id)
    {
        var bytes = Serialize(new { schemaVersion = 1, rootId = id.ToString("D") });
        try
        {
            using var stream = new FileStream(Path.Combine(dataDirectory, RootMarkerName), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException(
                "Хранилище не пусто либо storage-root.json невозможно создать без перезаписи.", exception);
        }
    }

    private static void PublishInstalledBootstrap(string installedBaseDirectory,
        string dataDirectory, Guid rootId)
    {
        try { Directory.CreateDirectory(installedBaseDirectory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException("Не удалось создать каталог installed bootstrap.", exception);
        }

        var destination = Path.Combine(installedBaseDirectory, BootstrapName);
        var temporary = Path.Combine(installedBaseDirectory, $".storage-{Guid.NewGuid():N}.tmp");
        var bytes = Serialize(new
        {
            schemaVersion = 1,
            dataDirectory,
            rootId = rootId.ToString("D")
        });
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteOwnTemporary(temporary);
            throw new StorageRootSelectionException(
                "Не удалось атомарно опубликовать installed bootstrap без перезаписи.", exception);
        }
    }

    private static IReadOnlyList<string> FindLegacyRoots(string executableDirectory, bool developmentMode)
    {
        var candidates = new List<string>
        {
            Path.Combine(executableDirectory, "data", "local"),
            Path.Combine(executableDirectory, "run-data", "local")
        };
        if (string.Equals(Path.GetFileName(executableDirectory), "app",
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            var parent = Directory.GetParent(executableDirectory)?.FullName;
            if (parent is not null)
                candidates.Add(Path.Combine(parent, "run-data", "local"));
        }

        var result = new List<string>();
        var unique = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var path = StoragePathGuards.RequireDirectoryPath(
                candidate, "Известный legacy-каталог", mustExist: false, developmentMode);
            if (unique.Add(path) && StoragePathGuards.HasAnyEntry(path, "Известный legacy-каталог"))
                result.Add(path);
        }
        return result;
    }

    private static StorageRootSelectionException LegacyFailure(StorageRootSelection selection) => new(
        $"Найдено прежнее хранилище; требуется явная проверяемая миграция: {string.Join(", ", selection.LegacyCandidates)}");

    private static byte[] Serialize<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));

    private static void TryDeleteOwnTemporary(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
