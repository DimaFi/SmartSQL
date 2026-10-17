namespace SmartSQL.Desktop.Storage;

/// <summary>
/// Read-only D056 preflight. Reads known configuration files and asks the pure
/// policy to select a candidate. Does not initialize roots, open a database,
/// write a bootstrap, or grant permission to trust a physical filesystem path.
/// </summary>
public static class StorageRootReadOnlyPreflight
{
    public static StorageRootSelection Inspect(StorageRootPreflightInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Explicit env overrides the other modes. Invalid/empty presence MUST
        // throw without reading or repairing portable/installed files.
        if (input.OverridePresent)
            return StorageRootSelectionPolicy.Select(new StorageRootSelectionInput(
                input.ExecutableDirectory, input.InstalledDefaultDataDirectory,
                OverridePresent: true, OverrideValue: input.OverrideValue));

        var executableDirectory = GetAbsoluteDirectory(input.ExecutableDirectory, "Каталог программы");
        var portableFile = Path.Combine(executableDirectory, "smartsql.portable.json");
        var portable = StorageManifestReader.ReadOptional(portableFile, StorageManifestKind.Portable);
        if (portable.IsPresent)
            return StorageRootSelectionPolicy.Select(new StorageRootSelectionInput(
                executableDirectory, input.InstalledDefaultDataDirectory,
                PortableMarkerPresent: true, PortableMarkerJson: portable.Json));

        var bootstrap = StorageManifestReader.ReadOptional(
            input.InstalledBootstrapFilePath, StorageManifestKind.InstalledBootstrap);
        if (bootstrap.IsPresent)
        {
            var selection = StorageRootSelectionPolicy.Select(new StorageRootSelectionInput(
                executableDirectory, input.InstalledDefaultDataDirectory,
                InstalledBootstrapPresent: true, InstalledBootstrapJson: bootstrap.Json));

            // A referenced but missing root identity is a startup failure, not
            // evidence of a first run. Root files are never regenerated here.
            var identity = StorageManifestReader.ReadRequired(
                Path.Combine(selection.DataDirectory!, "storage-root.json"),
                StorageManifestKind.RootIdentity);
            StorageRootSelectionPolicy.RequireMatchingInstalledRoot(selection, identity);
            return selection;
        }

        // A missing bootstrap is NOT proof of first use. An independent bounded
        // probe must explicitly report successful legacy discovery. We don't
        // silently scan every folder/disk or assume legacyRoots == null is empty.
        return StorageRootSelectionPolicy.Select(new StorageRootSelectionInput(
            executableDirectory, input.InstalledDefaultDataDirectory,
            LegacyRootsWithData: input.LegacyRootsWithData,
            LegacyDiscoveryCompleted: input.LegacyDiscoveryCompleted));
    }

    private static string GetAbsoluteDirectory(string? input, string label)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new StorageRootSelectionException($"{label}: требуется абсолютный путь.");
        try
        {
            if (!Path.IsPathFullyQualified(input))
                throw new StorageRootSelectionException($"{label}: требуется абсолютный путь.");
            return Path.GetFullPath(input);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new StorageRootSelectionException($"{label}: путь некорректен.", exception);
        }
    }
}

public sealed record StorageRootPreflightInput(
    string ExecutableDirectory,
    string InstalledDefaultDataDirectory,
    string InstalledBootstrapFilePath,
    bool OverridePresent = false,
    string? OverrideValue = null,
    IReadOnlyList<string>? LegacyRootsWithData = null,
    bool LegacyDiscoveryCompleted = false);
