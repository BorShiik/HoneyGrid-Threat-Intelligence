using System.Text.Json.Serialization;

namespace HoneyGrid.Contracts;

public class McpServerState
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    [JsonPropertyName("tools")]
    public required string[] Tools { get; init; }

    [JsonPropertyName("lastPing")]
    public required int LastPing { get; init; }

    [JsonPropertyName("requestsToday")]
    public required int RequestsToday { get; init; }
}
