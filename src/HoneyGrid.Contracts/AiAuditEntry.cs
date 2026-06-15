using System.Text.Json.Serialization;

namespace HoneyGrid.Contracts;

public class AiAuditEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("server")]
    public required string Server { get; init; }

    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    [JsonPropertyName("input")]
    public required string Input { get; init; }

    [JsonPropertyName("latencyMs")]
    public required int LatencyMs { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
