using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Rozgłaszanie REALNEJ telemetrii węzłów SDN do klientów (SignalR, target
/// "sdnTelemetry"). To NIE jest symulator — metryki (cpu/ram/ruch/połączenia)
/// pochodzą z agenta <c>HoneyGrid.Sensors.NodeMetrics</c>, który czyta /proc na
/// każdym VPS i zapisuje je w dokumencie węzła (kontener <c>sdnNodes</c>).
///
/// Funkcja tylko ODCZYTUJE te wartości i rozsyła je co 5 s — żadnych Random.
/// Węzły bez świeżych metryk (lub bez agenta) raportują 0 i status "offline",
/// gdy <c>lastSeen</c> jest starsze niż godzina.
/// </summary>
public sealed class SdnSimulator
{
    private readonly CosmosClient _cosmos;
    private readonly ILogger<SdnSimulator> _logger;
    private readonly string _databaseName;

    public SdnSimulator(CosmosClient cosmos, IConfiguration config, ILogger<SdnSimulator> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
    }

    [Function(nameof(BroadcastSdnTelemetry))]
    [SignalROutput(HubName = "attacks")]
    public async Task<SignalRMessageAction[]> BroadcastSdnTelemetry(
        [TimerTrigger("*/5 * * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var container = _cosmos.GetContainer(_databaseName, "sdnNodes");
        var nodes = new List<SdnNodeState>();

        try
        {
            var query = new QueryDefinition("SELECT * FROM c");
            using var iterator = container.GetItemQueryIterator<SdnNodeState>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                nodes.AddRange(page);
            }
        }
        catch (CosmosException ex)
        {
            // Kontener jeszcze nie istnieje (brak żadnych zdarzeń / agentów) — nic do rozgłoszenia.
            _logger.LogDebug(ex, "SDN: kontener sdnNodes niedostępny — pomijam rozgłaszanie.");
            return [];
        }

        if (nodes.Count == 0) return [];

        var now = DateTimeOffset.UtcNow;
        var events = new List<SdnTelemetryEvent>(nodes.Count);

        foreach (var node in nodes)
        {
            // Brak świeżych danych (godzina ciszy) → węzeł traktujemy jak offline,
            // zerujemy metryki, żeby UI nie pokazywało nieaktualnych wartości.
            var stale = node.LastSeen.HasValue && (now - node.LastSeen.Value) > TimeSpan.FromHours(1);
            if (node.Status == "offline" || stale)
            {
                events.Add(new SdnTelemetryEvent
                {
                    Id = node.Id, Cpu = 0, Ram = 0, FilteredTraffic = 0, Connections = 0,
                });
                continue;
            }

            // Realne metryki z dokumentu (zapisane przez agenta NodeMetrics).
            // Brak pola (agent jeszcze nie zaraportował) → 0, bez zmyślania.
            events.Add(new SdnTelemetryEvent
            {
                Id = node.Id,
                Cpu = node.Cpu ?? 0,
                Ram = node.Ram ?? 0,
                FilteredTraffic = node.FilteredTraffic ?? 0,
                Connections = node.Connections ?? 0,
            });
        }

        // Rozgłoszenie do klientów (target "sdnTelemetry").
        return [new SignalRMessageAction("sdnTelemetry", [events])];
    }
}
