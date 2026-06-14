using System.Text.Json;
using HoneyGrid.Contracts;
using HoneyGrid.Functions.Ai;

namespace HoneyGrid.Functions.Tests;

public class ClassificationPromptTests
{
    [Fact]
    public void System_prompt_lists_required_fields()
    {
        Assert.Contains("killChainPhase", ClassificationPrompt.System);
        Assert.Contains("sophistication", ClassificationPrompt.System);
        Assert.Contains("JSON", ClassificationPrompt.System);
    }

    [Fact]
    public void User_prompt_embeds_count_and_valid_json_payload()
    {
        var batch = new[]
        {
            TestEvents.Event("1.1.1.1", EventType.Command, command: "uname -a", country: "CN"),
            TestEvents.Event("2.2.2.2", username: "root", password: "123456", country: "RU"),
        };

        var prompt = ClassificationPrompt.BuildUser(batch);

        Assert.Contains("2", prompt);

        // The embedded payload must be valid JSON we can round-trip.
        var json = ClassificationResponseParser.ExtractJsonArray(prompt);
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("uname -a", doc.RootElement[0].GetProperty("command").GetString());
    }
}
