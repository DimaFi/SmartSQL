using SmartSQL.Desktop.Storage;

namespace SmartSQL.Desktop;

/// <summary>One approved, process-stable root. Resolving locations never creates a database.</summary>
internal static class WorkspacePaths
{
    private static StorageRuntimeLease? _lease;
    public static bool IsReady => _lease is not null;
    public static string? StartupError { get; private set; }
    public static string DataDirectory => _lease?.DataDirectory
        ?? throw new InvalidOperationException("Сначала необходимо выбрать доступное хранилище SmartSQL.");
    public static string DefaultDatabasePath => Path.Combine(DataDirectory, "smartsql.db");
    public static string DemoDatabasePath => Path.Combine(DataDirectory, "pedagogical-practice-demo.db");
    public static string GetDataDirectory() => DataDirectory;

    public static bool TryInitializeRuntime(string? selectedExistingRoot = null)
    {
        if (IsReady) return true;
        StorageRuntimeLease? pending = null;
        var previousEnvironment = new Dictionary<string, string?>();
        try
        {
            pending = StorageRuntimeRoot.Open(CreateRequest(selectedExistingRoot));
            var environment = new Dictionary<string, string?>
            {
                ["TEMP"] = pending.TemporaryDirectory,
                ["TMP"] = pending.TemporaryDirectory,
                ["TMPDIR"] = pending.TemporaryDirectory,
                ["XDG_CACHE_HOME"] = Path.Combine(pending.DataDirectory, "cache"),
                ["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(pending.DataDirectory, "dotnet-bundles")
            };
            foreach (var entry in environment)
            {
                previousEnvironment[entry.Key] = Environment.GetEnvironmentVariable(entry.Key);
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
            _lease = pending;
            StartupError = null;
            return true;
        }
        catch (Exception exception)
        {
            foreach (var entry in previousEnvironment)
            {
                try { Environment.SetEnvironmentVariable(entry.Key, entry.Value); }
                catch (Exception) { /* Process-only rollback; original startup failure is still displayed. */ }
            }
            pending?.Dispose();
            StartupError = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            return false;
        }
    }

    private static StorageRuntimeRequest CreateRequest(string? selectedExistingRoot)
    {
        var executable = Path.GetFullPath(AppContext.BaseDirectory);
        var development = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMARTSQL_WORKSPACE_ROOT"));
#if DEBUG
        development = true;
#endif
        var present = HasEnvironmentVariable("SMARTSQL_DATA_DIRECTORY");
        var configured = Environment.GetEnvironmentVariable("SMARTSQL_DATA_DIRECTORY") ?? "";
        if (selectedExistingRoot is not null)
        {
            present = true;
            configured = selectedExistingRoot;
        }
#if DEBUG
        if (!present)
        {
            var workspace = FindDevelopmentWorkspace();
            if (workspace is null)
                throw new InvalidOperationException("Debug-запуск требует явную тестовую папку на D: через scripts/Run.ps1 -DataDirectory.");
            present = true;
            configured = Path.Combine(workspace, "data", "local");
        }
#endif
        var installedBase = development
            ? Path.Combine(executable, ".unused-installed-storage")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify), "SmartSQL");
        var temporary = HasEnvironmentVariable("SMARTSQL_TEMP_DIRECTORY")
            ? Environment.GetEnvironmentVariable("SMARTSQL_TEMP_DIRECTORY") ?? "" : null;
        return new(executable, installedBase, present, configured, temporary, development,
            ExistingOnly: selectedExistingRoot is not null);
    }

    private static bool HasEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(key => string.Equals(key, name,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

#if DEBUG
    private static string? FindDevelopmentWorkspace()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SmartSQL.sln"))) return directory.FullName;
        return null;
    }
#endif

    public static void Shutdown()
    {
        _lease?.Dispose();
        _lease = null;
    }
}
