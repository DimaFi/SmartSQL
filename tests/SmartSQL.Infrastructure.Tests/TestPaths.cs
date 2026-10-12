using System.Runtime.CompilerServices;

namespace SmartSQL.Infrastructure.Tests;

internal static class TestPaths
{
    internal static string Root { get; } = FindRoot();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("TEMP", Root);
        Environment.SetEnvironmentVariable("TMP", Root);
        Environment.SetEnvironmentVariable("TMPDIR", Root);
    }

    private static string FindRoot()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTSQL_TEST_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured))
                throw new InvalidOperationException("SMARTSQL_TEST_DIRECTORY must be an absolute path.");
            return Path.GetFullPath(configured);
        }
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SmartSQL.sln")))
                return Path.Combine(directory.FullName, ".workspace", "test-temp");
        throw new InvalidOperationException("Set SMARTSQL_TEST_DIRECTORY when testing outside the checkout.");
    }
}
