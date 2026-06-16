using System.Collections.Concurrent;
using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using HoneyGrid.Contracts;
using HoneyGrid.Ingestion.Enrichment;
using HoneyGrid.Ingestion.Sinks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Ingestion;

/// <summary>
/// Główny worker ingestii: EventProcessorClient (Azure.Messaging.EventHubs.Processor)
/// z checkpointami w Blob Storage. Dla każdego zdarzenia:
///   deserializacja HoneypotEvent (HoneyGridJson) → pipeline wzbogacania
///   (GeoIP, rDNS, AbuseIPDB) → fan-out do sinków (Cosmos, Blob raw, Service Bus)
///   → checkpoint co CheckpointEveryEvents zdarzeń per partycja.
///
/// Bezkluczowo: wszystkie klienty z DefaultAzureCredential (lokalnie az login,
/// w chmurze Managed Identity; AZURE_CLIENT_ID honorowane automatycznie).
///
/// ODPORNOŚĆ (realny problem z deployu): zaraz po wdrożeniu role RBAC potrafią
/// propagować się kilka minut i pierwszy StartProcessingAsync kończy się 403.
/// Worker NIE może wtedy crash-loopować — błąd logujemy i ponawiamy start
/// z opóźnieniem, aż się uda albo host zostanie zatrzymany.
/// </summary>
public sealed class IngestionWorker(
    IOptions<IngestionOptions> options,
    EnrichmentPipeline pipeline,
    IEnumerable<IEventSinkTarget> sinks,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartRetryDelay = TimeSpan.FromSeconds(30);

    private readonly IngestionOptions _options = options.Value;
    private readonly IReadOnlyList<IEventSinkTarget> _sinks = sinks.ToList();
    private readonly ConcurrentDictionary<string, long> _partitionCounters = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.DryRun)
        {
            logger.LogWarning(
                "IngestionWorker w trybie DryRun — brak połączeń z Azure, worker bezczynny (tylko lokalny rozruch hosta).");
            await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            return;
        }

        // Pętla odporności: każdy błąd startu/działania procesora => log + ponowny start.
        while (!stoppingToken.IsCancellationRequested)
        {
            EventProcessorClient? processor = null;

            try
            {
                processor = CreateProcessor();
                processor.ProcessEventAsync += OnProcessEventAsync;
                processor.ProcessErrorAsync += OnProcessErrorAsync;

                await processor.StartProcessingAsync(stoppingToken);

                logger.LogInformation(
                    "Ingestia uruchomiona: {Namespace}/{Hub} (grupa {Group}), checkpointy w kontenerze '{Container}'.",
                    _options.EventHubFullyQualifiedNamespace,
                    _options.EventHubName,
                    _options.ConsumerGroup,
                    _options.CheckpointContainer);

                // Procesor pracuje w tle — czekamy na zatrzymanie hosta.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normalne zamknięcie hosta.
            }
            catch (Exception ex)
            {
                // Np. 403 podczas propagacji RBAC albo chwilowy błąd sieci —
                // logujemy i próbujemy ponownie zamiast wywracać kontener.
                logger.LogError(
                    ex,
                    "Błąd procesora Event Hub (możliwa propagacja uprawnień RBAC po deployu) — ponowny start za {Delay}s.",
                    StartRetryDelay.TotalSeconds);
            }
            finally
            {
                if (processor is not null)
                {
                    try
                    {
                        await processor.StopProcessingAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Błąd przy zatrzymywaniu procesora — ignoruję.");
                    }

                    processor.ProcessEventAsync -= OnProcessEventAsync;
                    processor.ProcessErrorAsync -= OnProcessErrorAsync;
                }
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(StartRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // zamykanie hosta
                }
            }
        }
    }

    /// <summary>Bezkluczowa konstrukcja procesora + magazynu checkpointów w Blob.</summary>
    private EventProcessorClient CreateProcessor()
    {
        var credential = new DefaultAzureCredential();

        var checkpointContainerUri = new Uri(
            $"{_options.BlobServiceUri!.TrimEnd('/')}/{_options.CheckpointContainer}");
        var checkpointStore = new BlobContainerClient(checkpointContainerUri, credential);

        return new EventProcessorClient(
            checkpointStore,
            _options.ConsumerGroup,
            _options.EventHubFullyQualifiedNamespace,
            _options.EventHubName,
            credential);
    }

    /// <summary>Obsługa pojedynczego zdarzenia z partycji.</summary>
    private async Task OnProcessEventAsync(ProcessEventArgs args)
    {
        try
        {
            if (!args.HasEvent)
            {
                return;
            }

            var json = args.Data.EventBody.ToString();
            var evt = HoneyGridJson.Deserialize(json);

            if (evt is null)
            {
                logger.LogWarning(
                    "Nie udało się zdeserializować zdarzenia z partycji {Partition} — pomijam (oryginał: {Json}).",
                    args.Partition.PartitionId, json);
            }
            else
            {
                // Drop health probes (127.0.0.1) from ACA ingress to save Cosmos DB costs
                if (evt.AttackerIp == "127.0.0.1")
                {
                    var c = _partitionCounters.AddOrUpdate(args.Partition.PartitionId, 1, (_, current) => current + 1);
                    if (c % _options.CheckpointEveryEvents == 0)
                    {
                        await args.UpdateCheckpointAsync(args.CancellationToken);
                    }
                    return;
                }

                // 1. Wzbogacanie (GeoIP, rDNS, AbuseIPDB) — każdy krok degraduje się łagodnie.
                evt = await pipeline.EnrichAsync(evt, args.CancellationToken);

                // 2. Referencja do surowego JSON-a w Blob — ścieżka jest deterministyczna,
                //    więc możemy ją wpisać do dokumentu zanim sam blob zostanie wgrany.
                evt = evt with { RawRef = $"{_options.RawContainer}/{RawBlobWriter.GetBlobName(evt)}" };

                // 3. Fan-out: błąd jednego sinka nie blokuje pozostałych.
                foreach (var sink in _sinks)
                {
                    try
                    {
                        await sink.WriteAsync(evt, args.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Sink {Sink} nie zapisał zdarzenia {EventId} — kontynuuję z pozostałymi.",
                            sink.Name, evt.Id);
                    }
                }
            }

            // 4. Checkpoint co N zdarzeń per partycja (oszczędność operacji Blob).
            var count = _partitionCounters.AddOrUpdate(args.Partition.PartitionId, 1, (_, c) => c + 1);
            if (count % _options.CheckpointEveryEvents == 0)
            {
                await args.UpdateCheckpointAsync(args.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Handler NIGDY nie może rzucić — to ubiłoby pompę partycji procesora.
            logger.LogError(ex, "Nieoczekiwany błąd przetwarzania zdarzenia z partycji {Partition}.", args.Partition.PartitionId);
        }
    }

    /// <summary>Błędy infrastrukturalne procesora: logujemy, nie wywracamy procesu.</summary>
    private Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Błąd procesora Event Hub (operacja: {Operation}, partycja: {Partition}) — procesor ponowi pracę sam.",
            args.Operation, args.PartitionId ?? "-");
        return Task.CompletedTask;
    }
}
