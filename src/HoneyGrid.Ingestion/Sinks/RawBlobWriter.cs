using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Audyt: zapis pełnego JSON-a wzbogaconego zdarzenia do Blob Storage,
/// kontener "raw", ścieżka {yyyy}/{MM}/{dd}/{sensorId}/{id}.json
/// (partycjonowanie po dacie zdarzenia i sensorze — wygodne do przeglądania i lifecycle policy).
/// Klient bezkluczowy (DefaultAzureCredential), tworzony leniwie przy pierwszym zapisie.
/// </summary>
public sealed class RawBlobWriter : IEventSinkTarget
{
    private readonly IngestionOptions _options;
    private readonly ILogger<RawBlobWriter> _logger;
    private readonly ResiliencePipeline _retry;
    private BlobContainerClient? _container;

    public RawBlobWriter(IOptions<IngestionOptions> options, ILogger<RawBlobWriter> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Polly: ponawiamy throttling i błędy przejściowe Storage (429/500/503).
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<RequestFailedException>(ex => ex.Status is 429 or 500 or 503)
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 4,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();
    }

    public string Name => "RawBlob";

    /// <summary>
    /// Deterministyczna nazwa bloba WEWNĄTRZ kontenera "raw" (czysta funkcja — testowalna).
    /// Kontener już nazywa się "raw", więc prefiksu "raw/" w nazwie bloba nie powtarzamy.
    /// </summary>
    public static string GetBlobName(HoneypotEvent evt)
    {
        var utc = evt.Timestamp.UtcDateTime;
        return $"{utc:yyyy}/{utc:MM}/{utc:dd}/{evt.SensorId}/{evt.Id}.json";
    }

    public async ValueTask WriteAsync(HoneypotEvent evt, CancellationToken ct)
    {
        var container = GetContainer();
        var blob = container.GetBlobClient(GetBlobName(evt));
        var payload = BinaryData.FromString(HoneyGridJson.Serialize(evt));

        await _retry.ExecuteAsync(
            async token => await blob.UploadAsync(payload, overwrite: true, token),
            ct);
    }

    /// <summary>Leniwa konstrukcja klienta kontenera (BlobServiceUri + nazwa kontenera).</summary>
    private BlobContainerClient GetContainer()
    {
        if (_container is not null)
        {
            return _container;
        }

        var containerUri = new Uri(
            $"{_options.BlobServiceUri!.TrimEnd('/')}/{_options.RawContainer}");

        _container = new BlobContainerClient(containerUri, new DefaultAzureCredential());

        _logger.LogInformation("RawBlobWriter połączony bezkluczowo z {Uri}.", containerUri);

        return _container;
    }
}
