using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartSql.LocalAssistant;

if (args is not ["benchmark", var profilesPath, var casesPath, ..])
{
    Console.Error.WriteLine("Usage: dotnet run --project src/SmartSql.LocalAssistant.Cli -- benchmark <profiles.json> <cases.json>");
    return 2;
}

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};
jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
var profiles = JsonSerializer.Deserialize<List<ModelProfile>>(await File.ReadAllTextAsync(profilesPath), jsonOptions)
               ?? throw new InvalidDataException("Model profiles are empty.");
var cases = JsonSerializer.Deserialize<List<VerificationCase>>(await File.ReadAllTextAsync(casesPath), jsonOptions)
            ?? throw new InvalidDataException("Verification cases are empty.");
var results = new List<ModelResult>();

foreach (var profile in profiles)
{
    using var client = new HttpClient { BaseAddress = new Uri(profile.Endpoint), Timeout = TimeSpan.FromMinutes(3) };
    if (!string.IsNullOrWhiteSpace(profile.ApiKey))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
    }

    var assistant = new LocalAssistantService(new OpenAiCompatibleLanguageModel(client, profile.Model));
    var measurements = new List<CaseResult>();
    foreach (var item in cases)
    {
        var request = item.ToRequest();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var decision = await assistant.InterpretAsync(request);
            stopwatch.Stop();
            var actual = decision switch
            {
                AcceptedDecision => "view",
                ExplainedDecision => "explain",
                RefusedDecision => "refuse",
                _ => "unknown"
            };
            measurements.Add(new CaseResult(item.Id, actual == item.ExpectedDecision, actual, stopwatch.Elapsed.TotalMilliseconds, null));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            stopwatch.Stop();
            measurements.Add(new CaseResult(item.Id, false, "error", stopwatch.Elapsed.TotalMilliseconds, exception.Message));
        }
    }

    var orderedLatency = measurements.Select(result => result.ElapsedMs).Order().ToArray();
    results.Add(new ModelResult(
        profile.Name,
        measurements.Count(result => result.Passed),
        measurements.Count,
        measurements.Average(result => result.ElapsedMs),
        Percentile95(orderedLatency),
        profile.EstimatedWorkingSetMiB,
        ReadWorkingSet(profile.ServerProcessId),
        measurements));
}

Console.WriteLine(JsonSerializer.Serialize(results, jsonOptions));
return results.All(result => result.Passed == result.Total) ? 0 : 1;

static double Percentile95(double[] values) =>
    values.Length == 0 ? 0 : values[(int)Math.Ceiling(values.Length * 0.95) - 1];

static double? ReadWorkingSet(int? processId)
{
    if (processId is null)
    {
        return null;
    }

    try
    {
        using var process = Process.GetProcessById(processId.Value);
        process.Refresh();
        return process.WorkingSet64 / 1024d / 1024d;
    }
    catch (ArgumentException)
    {
        return null;
    }
}

internal sealed record ModelProfile(
    string Name,
    string Endpoint,
    string Model,
    double? EstimatedWorkingSetMiB = null,
    int? ServerProcessId = null,
    string? ApiKey = null);

internal sealed record VerificationCase(
    string Id,
    string ExpectedDecision,
    string Request,
    CaseTable Table,
    CasePermissions Permissions)
{
    public AssistantRequest ToRequest() => new(
        new TableSchema(Table.Name, Table.Fields.Select(field => new FieldSchema(field.Name, field.Type)).ToArray()),
        new UserPermissions(Permissions.CanView, Permissions.ReadableFields.ToHashSet(StringComparer.OrdinalIgnoreCase), Permissions.MaximumRows),
        Request);
}

internal sealed record CaseTable(string Name, IReadOnlyList<CaseField> Fields);
internal sealed record CaseField(string Name, FieldType Type);
internal sealed record CasePermissions(bool CanView, IReadOnlyList<string> ReadableFields, int MaximumRows);
internal sealed record CaseResult(string Id, bool Passed, string ActualDecision, double ElapsedMs, string? Error);
internal sealed record ModelResult(
    string Model,
    int Passed,
    int Total,
    double AverageLatencyMs,
    double P95LatencyMs,
    double? EstimatedWorkingSetMiB,
    double? MeasuredWorkingSetMiB,
    IReadOnlyList<CaseResult> Cases);
