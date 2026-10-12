using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSql.LocalAssistant;

public sealed class OpenAiCompatibleLanguageModel(
    HttpClient httpClient,
    string model,
    double temperature = 0) : ILanguageModel
{
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            temperature,
            max_tokens = 512,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var response = await httpClient.PostAsJsonAsync("v1/chat/completions", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? throw new InvalidOperationException("Локальная модель вернула пустой ответ.");
    }
}
