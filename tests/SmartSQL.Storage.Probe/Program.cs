using System.Text.Json;
using SmartSQL.Desktop.Storage;

// Dedicated synthetic test process, never referenced or shipped by Desktop.
if (args.Length != 2) return 3;
var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!OnD(inputPath) || !OnD(outputPath)) return 3;
try
{
    var input = JsonSerializer.Deserialize<ProbeInput>(File.ReadAllText(inputPath))!;
    if (!OnD(input.App) || !OnD(input.InstalledBase) || input.Data is not null && !OnD(input.Data)) return 3;
    using var lease = StorageRuntimeRoot.Open(new StorageRuntimeRequest(
        input.App, input.InstalledBase, input.Data is not null, input.Data, DevelopmentMode: true,
        Checkpoint: stage =>
        {
            if (stage == "root-marker-published" && input.PauseAfterMarker ||
                stage == "root-prepared" && input.PauseBeforeMarker)
            {
                Publish(new { stage });
                Thread.Sleep(TimeSpan.FromSeconds(45));
            }
        }));
    Publish(new { stage = "opened", data = lease.DataDirectory, kind = lease.Kind.ToString() });
    if (input.Hold) Thread.Sleep(TimeSpan.FromSeconds(45));
    return 0;
}
catch (Exception exception)
{
    Publish(new { stage = "refused", error = exception.Message });
    return 2;
}

void Publish<T>(T value)
{
    var temporary = outputPath + ".tmp";
    File.WriteAllText(temporary, JsonSerializer.Serialize(value));
    File.Move(temporary, outputPath, overwrite: true);
}

static bool OnD(string path) => Path.IsPathFullyQualified(path) &&
    (!OperatingSystem.IsWindows() || string.Equals(Path.GetPathRoot(path), @"D:\", StringComparison.OrdinalIgnoreCase));

internal sealed record ProbeInput(string App, string InstalledBase, string? Data, bool Hold, bool PauseAfterMarker, bool PauseBeforeMarker);
