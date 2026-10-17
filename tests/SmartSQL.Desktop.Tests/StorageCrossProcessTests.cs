using System.Diagnostics;
using System.Text.Json;
using SmartSQL.Desktop.Storage;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class StorageCrossProcessTests : IDisposable
{
    private readonly string _directory;
    private readonly List<Process> _children = [];

    public StorageCrossProcessTests()
    {
        var basePath = Environment.GetEnvironmentVariable("SMARTSQL_TEST_DIRECTORY")
            ?? throw new InvalidOperationException("Run with scripts/Test.ps1.");
        if (OperatingSystem.IsWindows() && !string.Equals(Path.GetPathRoot(basePath), @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synthetic test files must stay on D:.");
        _directory = Path.Combine(basePath, "storage-process", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_directory, "app"));
    }

    [Fact]
    public void SecondProcessRefusesBusyRootAndAfterKillReopensSameIdentity()
    {
        var data = Path.Combine(_directory, "data");
        var first = Start(data, hold: true);
        Assert.Equal("opened", Result(first).GetProperty("stage").GetString());
        var original = File.ReadAllBytes(Path.Combine(data, "storage-root.json"));
        var second = Start(data);
        Assert.Equal("refused", Result(second).GetProperty("stage").GetString());
        WaitExit(second.Process, expected: 2);
        Stop(first.Process);
        var third = Start(data);
        Assert.Equal("opened", Result(third).GetProperty("stage").GetString());
        WaitExit(third.Process, expected: 0);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(data, "storage-root.json")));
        Assert.False(File.Exists(Path.Combine(data, "smartsql.db")));
    }

    [Fact]
    public void KilledBeforeMarkerLeavesEmptyRootAndCanStartAgain()
    {
        var first = Start(data: null, pauseBeforeMarker: true);
        Assert.Equal("root-prepared", Result(first).GetProperty("stage").GetString());
        var data = Path.Combine(_directory, "installed", "data");
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
        Stop(first.Process);
        var second = Start(data: null);
        Assert.Equal("opened", Result(second).GetProperty("stage").GetString());
        WaitExit(second.Process, expected: 0);
        Assert.True(File.Exists(Path.Combine(_directory, "installed", "storage.json")));
        Assert.False(File.Exists(Path.Combine(data, "smartsql.db")));
    }

    [Fact]
    public void KilledInitializerLeavesOrphanMarkerAndNextProcessRefusesAdoption()
    {
        var first = Start(data: null, pauseAfterMarker: true);
        Assert.Equal("root-marker-published", Result(first).GetProperty("stage").GetString());
        var basePath = Path.Combine(_directory, "installed");
        var marker = Path.Combine(basePath, "data", "storage-root.json");
        var original = File.ReadAllBytes(marker);
        Assert.False(File.Exists(Path.Combine(basePath, "storage.json")));
        Stop(first.Process);
        var second = Start(data: null);
        Assert.Equal("refused", Result(second).GetProperty("stage").GetString());
        WaitExit(second.Process, expected: 2);
        Assert.Equal(original, File.ReadAllBytes(marker));
        Assert.False(File.Exists(Path.Combine(basePath, "storage.json")));
        Assert.False(File.Exists(Path.Combine(basePath, "data", "smartsql.db")));
    }

    [Fact]
    public void TwoInstalledInitializersPublishOneBootstrapAndLaterReuseIt()
    {
        var first = Start(data: null, hold: true);
        var second = Start(data: null, hold: true);
        var results = new[] { Result(first), Result(second) };
        Assert.Single(results, r => r.GetProperty("stage").GetString() == "opened");
        Assert.Single(results, r => r.GetProperty("stage").GetString() == "refused");
        var bootstrap = Path.Combine(_directory, "installed", "storage.json");
        var original = File.ReadAllBytes(bootstrap);
        Stop(first.Process); Stop(second.Process);
        var third = Start(data: null);
        Assert.Equal("opened", Result(third).GetProperty("stage").GetString());
        WaitExit(third.Process, expected: 0);
        Assert.Equal(original, File.ReadAllBytes(bootstrap));
        using var json = JsonDocument.Parse(original);
        var data = json.RootElement.GetProperty("dataDirectory").GetString()!;
        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "storage-root.json")));
        Assert.Equal(json.RootElement.GetProperty("rootId").GetString(), identity.RootElement.GetProperty("rootId").GetString());
    }

    private (Process Process, string ResultFile) Start(string? data, bool hold = false, bool pauseAfterMarker = false,
        bool pauseBeforeMarker = false)
    {
        var id = Guid.NewGuid().ToString("N");
        var input = Path.Combine(_directory, id + ".input.json");
        var output = Path.Combine(_directory, id + ".output.json");
        File.WriteAllText(input, JsonSerializer.Serialize(new
        {
            App = Path.Combine(_directory, "app"), InstalledBase = Path.Combine(_directory, "installed"),
            Data = data, Hold = hold, PauseAfterMarker = pauseAfterMarker, PauseBeforeMarker = pauseBeforeMarker
        }));
        var assembly = Path.Combine(SourceRoot(), "tests", "SmartSQL.Storage.Probe", "bin",
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "net10.0", "SmartSQL.Storage.Probe.dll");
        Assert.True(File.Exists(assembly), "Build must include the storage probe project.");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _directory
        };
        start.ArgumentList.Add(assembly); start.ArgumentList.Add(input); start.ArgumentList.Add(output);
        var process = Process.Start(start)!;
        _children.Add(process);
        return (process, output);
    }

    private static JsonElement Result((Process Process, string ResultFile) child)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(child.ResultFile) && elapsed.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (child.Process.HasExited) break;
            Thread.Sleep(20);
        }
        Assert.True(File.Exists(child.ResultFile), "Child process produced no bounded storage result.");
        using var json = JsonDocument.Parse(File.ReadAllText(child.ResultFile));
        return json.RootElement.Clone();
    }

    private static void WaitExit(Process child, int expected)
    {
        Assert.True(child.WaitForExit(10_000), "Child failed to exit within deadline.");
        Assert.Equal(expected, child.ExitCode);
    }

    private static void Stop(Process child)
    {
        if (!child.HasExited) child.Kill(entireProcessTree: true);
        Assert.True(child.WaitForExit(10_000));
    }

    private static string SourceRoot()
    {
        for (var item = new DirectoryInfo(AppContext.BaseDirectory); item is not null; item = item.Parent)
            if (File.Exists(Path.Combine(item.FullName, "SmartSQL.sln"))) return item.FullName;
        throw new InvalidOperationException("Source workspace missing.");
    }

    public void Dispose()
    {
        foreach (var child in _children)
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10_000); }
            child.Dispose();
        }
        // Only this test's fixed synthetic subtree, never a user-selected target.
        Directory.Delete(_directory, recursive: true);
    }
}
