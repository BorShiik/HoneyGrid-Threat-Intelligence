using System.Text.Json.Serialization;

namespace HoneyGrid.Contracts;

public class SdnNodeState
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("location")]
    public required string Location { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("dynamicMigration")]
    public bool DynamicMigration { get; set; }
}
