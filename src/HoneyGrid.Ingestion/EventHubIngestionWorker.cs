using Azure.Messaging.EventHubs.Consumer;
using HoneyGrid.Contracts;

namespace HoneyGrid.Ingestion;

/// <summary>
/// Konsument Event Hub + pipeline wzbogacania zdarzeń — szkielet.
/// Docelowo: EventProcessorClient z checkpointami, deserializacja HoneypotEvent,
/// wzbogacenie GeoIP/TI, klasyfikacja i zapis do Cosmos DB.
/// </summary>
public sealed class EventHubIngestionWorker(
    ILogger<EventHubIngestionWorker> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration["EventHub:ConnectionString"];
        var hubName = configuration["EventHub:Name"] ?? "honeypot-events";

        if (string.IsNullOrEmpty(connectionString))
        {
            // Tryb lokalny bez Event Huba — szkielet działa, ale nic nie konsumuje.
            logger.LogWarning("Brak konfiguracji EventHub:ConnectionString — konsument nieaktywny.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        // TODO (Track B, Tydzień 3): zamienić EventHubConsumerClient na EventProcessorClient
        //                            (checkpointy w Blob Storage, skalowanie na partycje).
        await using var consumer = new EventHubConsumerClient(
            EventHubConsumerClient.DefaultConsumerGroupName,
            connectionString,
            hubName);

        logger.LogInformation("Start konsumpcji z Event Hub '{HubName}'", hubName);

        await foreach (var partitionEvent in consumer.ReadEventsAsync(stoppingToken))
        {
            var json = partitionEvent.Data.EventBody.ToString();
            var evt = HoneyGridJson.Deserialize(json);
            if (evt is null)
            {
                logger.LogWarning("Nie udało się zdeserializować zdarzenia: {Json}", json);
                continue;
            }

            // TODO (Track B, Tydzień 4): pipeline wzbogacania:
            //   1. GeoIP (MaxMind / IPinfo)        -> evt.Geo
            //   2. Threat intel (AbuseIPDB)        -> evt.ThreatIntel
            //   3. Klasyfikacja (kill chain)       -> evt.Classification
            //   4. Zapis do Cosmos DB + Blob (rawRef)
            logger.LogInformation(
                "Odebrano zdarzenie {EventId} z {AttackerIp} ({EventType})",
                evt.Id, evt.AttackerIp, evt.EventType);
        }
    }
}
