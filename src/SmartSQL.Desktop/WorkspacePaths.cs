namespace SmartSQL.Desktop;

/// <summary>Portable, explicit locations for application-created local files.</summary>
internal static class WorkspacePaths
{
    public static string DataDirectory => GetDataDirectory();
    public static string DefaultDatabasePath => Path.Combine(DataDirectory, "smartsql.db");
    public static string DemoDatabasePath => Path.Combine(DataDirectory, "pedagogical-practice-demo.db");

    public static string GetDataDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTSQL_DATA_DIRECTORY");
        var directory = configured is null
            ? Path.Combine(FindWorkspaceDirectory() ?? AppContext.BaseDirectory, "data", "local")
            : GetConfiguredDirectory(configured, "SMARTSQL_DATA_DIRECTORY");
        return EnsureWritableDirectory(directory);
    }

    /// <summary>Also directs process-local temporary files to the chosen data location.</summary>
    public static void InitializeRuntime()
    {
        var dataDirectory = GetDataDirectory();
        var configured = Environment.GetEnvironmentVariable("SMARTSQL_TEMP_DIRECTORY");
        var temporaryDirectory = EnsureWritableDirectory(configured is null
            ? Path.Combine(dataDirectory, "temp")
            : GetConfiguredDirectory(configured, "SMARTSQL_TEMP_DIRECTORY"));

        Environment.SetEnvironmentVariable("TEMP", temporaryDirectory);
        Environment.SetEnvironmentVariable("TMP", temporaryDirectory);
        Environment.SetEnvironmentVariable("TMPDIR", temporaryDirectory);
    }

    private static string? FindWorkspaceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartSQL.sln")))
                return directory.FullName;
        }

        return null;
    }

    private static string GetConfiguredDirectory(string directory, string variableName)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException($"{variableName} must contain an absolute directory path.");

        return Path.GetFullPath(directory);
    }

    private static string EnsureWritableDirectory(string directory)
    {
        var path = Path.GetFullPath(directory);
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, $".smartsql-write-check-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
        return path;
    }
}
