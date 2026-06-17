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

        var now = DateTimeOffset.UtcNow;

        if (_lastWritten.TryGetValue(evt.SensorId, out var lastTime))
        {
            if (now - lastTime < _debounceInterval)
            {
                // Pomijamy zapis, jeśli niedawno odświeżyliśmy ten węzeł
                return;
            }
        }

        var container = GetContainer();

        var location = ParseLocationFromSensorId(evt.SensorId);
        
        var node = new SdnNodeState
        {
            Id = evt.SensorId,
            Name = evt.SensorId,
            Location = location,
            Status = "active",
            DynamicMigration = false,
            LastSeen = evt.Timestamp
        };

        try
        {
            await _retry.ExecuteAsync(
                async token => await container.UpsertItemAsync(
                    node,
                    new PartitionKey(node.Id),
                    cancellationToken: token),
                ct);

            _lastWritten[evt.SensorId] = now;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zaktualizować statusu węzła SDN {SensorId}", evt.SensorId);
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
