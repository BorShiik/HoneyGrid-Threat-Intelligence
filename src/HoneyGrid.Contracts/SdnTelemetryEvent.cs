using System.Text.Json.Serialization;

namespace HoneyGrid.Contracts;

public class SdnTelemetryEvent
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("cpu")]
    public required int Cpu { get; init; }

    [JsonPropertyName("ram")]
    public required int Ram { get; init; }

    [JsonPropertyName("filteredTraffic")]
    public required int FilteredTraffic { get; init; }

    [JsonPropertyName("connections")]
    public required int Connections { get; init; }
}
