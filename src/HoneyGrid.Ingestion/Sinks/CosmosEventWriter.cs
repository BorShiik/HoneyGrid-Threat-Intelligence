using Azure.Identity;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Hot path: upsert wzbogaconego zdarzenia do Cosmos DB (baza honeygrid / kontener events,
/// klucz partycji /attackerIp). Klient bezkluczowy: endpoint + DefaultAzureCredential.
///
/// KLUCZOWE — serializacja: dokument MUSI mieć camelCase i małe "id", inaczej Cosmos
/// odrzuci zapis. Używamy <see cref="CosmosClientOptions.UseSystemTextJsonSerializerWithOptions"/>
/// z dokładnie tymi samymi opcjami co reszta platformy (<see cref="HoneyGridJson.Options"/>).
///
/// UWAGA dla Track B: zawsze zapisujemy PEŁNY dokument bazowy (upsert) — klasyfikator AI
/// Track B później PATCH-uje wyłącznie właściwość "classification", więc nie ma wyścigu
/// nadpisania klasyfikacji przez ponowny zapis bazy.
///
/// Klient tworzony leniwie przy pierwszym zapisie — konstrukcja sinka w DI nie dotyka
/// sieci (ważne dla DryRun i okresu propagacji RBAC po deployu).
/// </summary>
public sealed class CosmosEventWriter : IEventSinkTarget, IDisposable
{
    private readonly IngestionOptions _options;
    private readonly ILogger<CosmosEventWriter> _logger;
    private readonly ResiliencePipeline _retry;
    private CosmosClient? _client;
    private Container? _container;

    public CosmosEventWriter(IOptions<IngestionOptions> options, ILogger<CosmosEventWriter> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Polly: ponawiamy throttling (429), niedostępność (503) i timeouty (408).
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<CosmosException>(ex =>
                        ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.RequestTimeout),
                MaxRetryAttempts = 4,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Ponawiam zapis do Cosmos (próba {Attempt}): {Error}",
                        args.AttemptNumber + 1, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    public string Name => "CosmosDB";

    public async ValueTask WriteAsync(HoneypotEvent evt, CancellationToken ct)
    {
        var container = GetContainer();

        await _retry.ExecuteAsync(
            async token => await container.UpsertItemAsync(
                evt,
                new PartitionKey(evt.AttackerIp),
                cancellationToken: token),
            ct);
    }

    /// <summary>Leniwa konstrukcja klienta — bez połączenia w czasie rejestracji DI.</summary>
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
                // Serializacja zgodna z kontraktem platformy (camelCase, null pomijane,
                // enumy jako stringi) — gwarantuje małe "id" i "attackerIp" (ścieżka PK).
                UseSystemTextJsonSerializerWithOptions = HoneyGridJson.Options,
            });

        _container = _client.GetContainer(_options.CosmosDatabase, _options.CosmosEventsContainer);

        _logger.LogInformation(
            "CosmosEventWriter połączony bezkluczowo z {Endpoint} ({Db}/{Container}).",
            _options.CosmosEndpoint, _options.CosmosDatabase, _options.CosmosEventsContainer);

        return _container;
    }

    public void Dispose() => _client?.Dispose();
}
