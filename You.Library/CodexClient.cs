using System.Text;
using System.Text.Json;

namespace You.Library;

public sealed class CodexClient
{
    private const string ResponsesApiUrl = "https://api.openai.com/v1/responses";
    private readonly HttpClient httpClient;

    public CodexClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> GetResponseAsync(
        string prompt,
        string apiKey,
        string model = "gpt-5-codex",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }

        var payload = BuildResponsesApiPayload(model, prompt);
        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesApiUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Codex request failed with status {(int)response.StatusCode}: {responseBody}");
        }

        if (!TryExtractAssistantText(responseBody, out var assistantText))
        {
            throw new InvalidOperationException("Codex response did not contain assistant text.");
        }

        return assistantText;
    }

    public static string BuildResponsesApiPayload(string model, string prompt)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model cannot be empty.", nameof(model));
        }

        var payload = new
        {
            model,
            input = prompt
        };

        return JsonSerializer.Serialize(payload);
    }

    public static bool TryExtractAssistantText(string responseJson, out string assistantText)
    {
        assistantText = string.Empty;

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return false;
        }

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputTextElement) &&
            outputTextElement.ValueKind == JsonValueKind.String)
        {
            assistantText = outputTextElement.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(assistantText);
        }

        if (!root.TryGetProperty("output", out var outputElement) || outputElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var outputItem in outputElement.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentElement.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var textElement) &&
                    textElement.ValueKind == JsonValueKind.String)
                {
                    assistantText = textElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(assistantText))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
