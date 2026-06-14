using System.Text.Json.Serialization;

namespace HoneyGrid.Functions.Profiling;

/// <summary>
/// Dokument aktora zapisywany do kontenera Cosmos <c>actors</c> (PK <c>/id</c>).
///
/// Kształt JSON celowo zgodny z kontacktem dashboardu (frontendowy typ ThreatActor
/// oraz <c>ThreatActorDto</c> w HoneyGrid.Api). Pola dodatkowe (asns) są przez
/// API/ front ignorowane, ale przydają się do dalszej analizy / debug.
/// </summary>
public sealed record ActorDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("firstSeen")]
    public required DateTimeOffset FirstSeen { get; init; }

    [JsonPropertyName("lastSeen")]
    public required DateTimeOffset LastSeen { get; init; }

    [JsonPropertyName("eventCount")]
    public required long EventCount { get; init; }

    [JsonPropertyName("knownIps")]
    public required IReadOnlyList<string> KnownIps { get; init; }

    [JsonPropertyName("countries")]
    public required IReadOnlyList<string> Countries { get; init; }

    [JsonPropertyName("asns")]
    public IReadOnlyList<string> Asns { get; init; } = [];

    /// <summary>minimal | intermediate | advanced.</summary>
    [JsonPropertyName("sophistication")]
    public required string Sophistication { get; init; }

    /// <summary>opportunistic | targeted | automated.</summary>
    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    /// <summary>critical | high | medium | low.</summary>
    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
