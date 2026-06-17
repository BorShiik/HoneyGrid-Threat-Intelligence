using System.Collections.Concurrent;
using Azure.Identity;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Dynamicznie rejestruje lub aktualizuje sensory w kolekcji sdnNodes Cosmos DB.
/// Używa mechanizmu debouncing w pamięci, by uniknąć wyczerpania RU przy masowych atakach
/// (zapisuje dany sensorId nie częściej niż raz na minutę).
/// </summary>
public sealed class CosmosSdnNodeWriter : IEventSinkTarget, IDisposable
{
    private readonly IngestionOptions _options;
    private readonly ILogger<CosmosSdnNodeWriter> _logger;
    private readonly ResiliencePipeline _retry;
    private CosmosClient? _client;
    private Container? _container;
    
    // Zabezpieczenie przed nadmiernym obciążeniem bazy - debouncing 1 min per sensorId
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastWritten = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMinutes(1);

    public CosmosSdnNodeWriter(IOptions<IngestionOptions> options, ILogger<CosmosSdnNodeWriter> logger)
    {
        _options = options.Value;
        _logger = logger;

        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<CosmosException>(ex =>
                        ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.RequestTimeout),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();
    }

    public string Name => "CosmosSdnNodes";

    public async ValueTask WriteAsync(HoneypotEvent evt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.SensorId))
        {
            return;
        }

        // Węzeł SDN = fizyczny VPS, nie pojedynczy sensor. Wszystkie sensory na
        // jednym VPS (cowrie-vps-frankfurt, web-vps-frankfurt, tcp-vps-frankfurt)
        // mapują się na ten sam węzeł "node-frankfurt", który agent NodeMetrics
        // wzbogaca o realne metryki hosta.
        var site = ParseLocationFromSensorId(evt.SensorId);
        var nodeId = $"node-{site}";
        var nodeName = $"Edge-{site}";

        var now = DateTimeOffset.UtcNow;

        if (_lastWritten.TryGetValue(nodeId, out var lastTime) && now - lastTime < _debounceInterval)
        {
            // Pomijamy zapis, jeśli niedawno odświeżyliśmy ten węzeł.
            return;
        }

        var container = GetContainer();

        // Aktualizujemy WYŁĄCZNIE pola żywotności (status/lastSeen/location/name)
        // operacją PATCH — pola metryk (cpu/ram/filteredTraffic/connections) należą
        // do agenta NodeMetrics i NIE są tu nadpisywane.
        var patch = new[]
        {
            PatchOperation.Set("/status", "active"),
            PatchOperation.Set("/lastSeen", evt.Timestamp),
            PatchOperation.Set("/location", site),
            PatchOperation.Set("/name", nodeName),
        };

        try
        {
            await _retry.ExecuteAsync(
                async token => await container.PatchItemAsync<SdnNodeState>(
                    nodeId, new PartitionKey(nodeId), patch, cancellationToken: token),
                ct);

            _lastWritten[nodeId] = now;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Pierwsze wystąpienie tego węzła — tworzymy dokument bazowy (bez metryk).
            var node = new SdnNodeState
            {
                Id = nodeId,
                Name = nodeName,
                Location = site,
                Status = "active",
                DynamicMigration = false,
                LastSeen = evt.Timestamp,
            };

            try
            {
                await _retry.ExecuteAsync(
                    async token => await container.UpsertItemAsync(
                        node, new PartitionKey(nodeId), cancellationToken: token),
                    ct);

                _lastWritten[nodeId] = now;
            }
            catch (Exception createEx)
            {
                _logger.LogWarning(createEx, "Nie udało się utworzyć węzła SDN {NodeId}", nodeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zaktualizować statusu węzła SDN {NodeId}", nodeId);
        }
    }

    private static string ParseLocationFromSensorId(string sensorId)
    {
        // Naiwne przypisanie lokacji na podstawie konwencji nazywania np. "cowrie-vps-frankfurt" -> "frankfurt"
        var parts = sensorId.Split('-');
        if (parts.Length > 0)
        {
            return parts[^1].ToLowerInvariant();
        }
        return "unknown";
    }

    private Container GetContainer()
    {
        if (_container is not null)
        {
            return _container;
        }

        _client = new CosmosClient(
            _options.CosmosEndpoint,
            new DefaultAzureCredential(),
            new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = HoneyGridJson.Options,
            });

        _container = _client.GetContainer(_options.CosmosDatabase, _options.CosmosSdnNodesContainer);
        return _container;
    }

    public void Dispose() => _client?.Dispose();
}
