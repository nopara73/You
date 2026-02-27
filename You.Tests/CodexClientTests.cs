using System.Text.Json;
using You.Library;

namespace You.Tests;

public class CodexClientTests
{
    [Fact]
    public void BuildResponsesApiPayload_IncludesModelAndPrompt()
    {
        var payload = CodexClient.BuildResponsesApiPayload("gpt-5-codex", "Write a unit test.");
        using var document = JsonDocument.Parse(payload);

        Assert.Equal("gpt-5-codex", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("Write a unit test.", document.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public void TryExtractAssistantText_ReadsOutputTextProperty()
    {
        const string responseJson = """
        {
          "id": "resp_123",
          "output_text": "Hello from Codex"
        }
        """;

        var success = CodexClient.TryExtractAssistantText(responseJson, out var assistantText);

        Assert.True(success);
        Assert.Equal("Hello from Codex", assistantText);
    }

    [Fact]
    public void TryExtractAssistantText_ReadsNestedContentTextProperty()
    {
        const string responseJson = """
        {
          "id": "resp_456",
          "output": [
            {
              "type": "message",
              "content": [
                {
                  "type": "output_text",
                  "text": "Nested text payload"
                }
              ]
            }
          ]
        }
        """;

        var success = CodexClient.TryExtractAssistantText(responseJson, out var assistantText);

        Assert.True(success);
        Assert.Equal("Nested text payload", assistantText);
    }
}
