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
    public required string Status { get; set; }

    [JsonPropertyName("dynamicMigration")]
    public bool DynamicMigration { get; set; }

    [JsonPropertyName("lastSeen")]
    public DateTimeOffset? LastSeen { get; set; }

    // ── Realna telemetria hosta (zapisuje agent HoneyGrid.Sensors.NodeMetrics
    //    z VPS przez PATCH). Nullable: dopóki agent nie zaraportuje, pola są
    //    nieobecne, a UI pokazuje "—" zamiast zmyślonych liczb. ──────────────

    /// <summary>Realne użycie CPU hosta w % (z /proc/stat). null = brak danych.</summary>
    [JsonPropertyName("cpu")]
    public int? Cpu { get; set; }

    /// <summary>Realne użycie RAM hosta w % (z /proc/meminfo). null = brak danych.</summary>
    [JsonPropertyName("ram")]
    public int? Ram { get; set; }

    /// <summary>Realny ruch sieciowy hosta: pakiety/s (z /proc/net/dev). null = brak danych.</summary>
    [JsonPropertyName("filteredTraffic")]
    public int? FilteredTraffic { get; set; }

    /// <summary>Realna liczba aktywnych połączeń TCP ESTABLISHED (z /proc/net/tcp). null = brak danych.</summary>
    [JsonPropertyName("connections")]
    public int? Connections { get; set; }
}
