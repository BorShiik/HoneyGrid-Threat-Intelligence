namespace HoneyGrid.Sensors.NodeMetrics;

/// <summary>Konfiguracja agenta metryk hosta (sekcja "NodeMetrics", env NodeMetrics__&lt;Nazwa&gt;).</summary>
public sealed class NodeMetricsOptions
{
    public const string SectionName = "NodeMetrics";

    /// <summary>Endpoint Cosmos DB, np. "https://....documents.azure.com:443/".</summary>
    public string? CosmosEndpoint { get; set; }

    /// <summary>Nazwa bazy Cosmos DB.</summary>
    public string CosmosDatabase { get; set; } = "honeygrid";

    /// <summary>Kontener węzłów SDN (klucz partycji /id).</summary>
    public string SdnNodesContainer { get; set; } = "sdnNodes";

    /// <summary>
    /// Lokacja/site tego VPS (np. "frankfurt"). Węzeł SDN = "node-&lt;site&gt;" — ten sam,
    /// który rejestrują sensory (CosmosSdnNodeWriter z evt.SensorId "cowrie-vps-frankfurt").
    /// </summary>
    public string Site { get; set; } = "unknown";

    /// <summary>Czytelna nazwa węzła; domyślnie "Edge-&lt;site&gt;".</summary>
    public string? NodeName { get; set; }

    /// <summary>Co ile sekund raportować metryki (min. 5).</summary>
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>Ścieżka do /proc. Aby mierzyć HOST (a nie kontener), podmontuj /proc hosta i ustaw np. "/host/proc".</summary>
    public string ProcPath { get; set; } = "/proc";
}
