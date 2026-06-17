using System.Net;
using Azure.Identity;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Projekcja sesji (Session Replay): dla każdego zdarzenia z <c>SessionId</c> utrzymuje
/// jeden dokument sesji w Cosmos (baza honeygrid / kontener "sessions", klucz partycji
/// /sessionId). Dokument scala metadane wielu zdarzeń tej samej sesji:
///   connect → startedAt; command → commandCount++; *.closed z ttylog → ttyRef/hasTty;
///   dowolne → endedAt/durationMs, geo, sensorId, attackerIp.
///
/// To brakujące ogniwo łańcucha replay: shipper wgrywa binarne TTY do Blob i ustawia
/// ttyRef na zdarzeniu, a ten sink zapisuje sesję z ttyRef do Cosmos, skąd czyta ją
/// endpoint GET /api/sessions/{id}/replay.
///
/// Współbieżność: read-modify-write z optymistyczną kontrolą wersji (ETag). Konflikt
/// (412/409) → ponawiamy całą operację na świeżym dokumencie, więc delta zdarzenia
/// (np. inkrement commandCount) nie ginie ani nie dubluje się.
/// </summary>
public sealed class CosmosSessionWriter : IEventSinkTarget, IDisposable
{
    private const int MaxConcurrencyAttempts = 5;

    private readonly IngestionOptions _options;
    private readonly ILogger<CosmosSessionWriter> _logger;
    private readonly ResiliencePipeline _retry;
    private CosmosClient? _client;
    private Container? _container;

    public CosmosSessionWriter(IOptions<IngestionOptions> options, ILogger<CosmosSessionWriter> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Polly: ponawiamy throttling (429), niedostępność (503), timeouty (408).
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<CosmosException>(ex =>
                        ex.StatusCode is HttpStatusCode.TooManyRequests
                            or HttpStatusCode.ServiceUnavailable
                            or HttpStatusCode.RequestTimeout),
                MaxRetryAttempts = 4,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();
    }

    public string Name => "CosmosSessions";

    public async ValueTask WriteAsync(HoneypotEvent evt, CancellationToken ct)
    {
        // Projektujemy tylko zdarzenia powiązane z sesją (SSH/Telnet Cowrie).
        if (string.IsNullOrWhiteSpace(evt.SessionId))
        {
            return;
        }

        var sessionId = evt.SessionId;
        var pk = new PartitionKey(sessionId);
        var container = GetContainer();

        await _retry.ExecuteAsync(async token =>
        {
            for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
            {
                SessionProjection doc;
                string? etag = null;
                bool exists;

                try
                {
                    var read = await container.ReadItemAsync<SessionProjection>(sessionId, pk, cancellationToken: token);
                    doc = read.Resource;
                    etag = read.ETag;
                    exists = true;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    doc = new SessionProjection
                    {
                        Id = sessionId,
                        SessionId = sessionId,
                        StartedAt = evt.Timestamp,
                        EndedAt = evt.Timestamp,
                    };
                    exists = false;
                }

                Merge(doc, evt);

                try
                {
                    if (exists)
                    {
                        await container.ReplaceItemAsync(
                            doc, sessionId, pk,
                            new ItemRequestOptions { IfMatchEtag = etag },
                            token);
                    }
                    else
                    {
                        await container.CreateItemAsync(doc, pk, cancellationToken: token);
                    }

                    return; // sukces
                }
                catch (CosmosException ex) when (
                    ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
                {
                    // Inny zapis wyprzedził nas — ponawiamy na świeżym dokumencie.
                    if (attempt == MaxConcurrencyAttempts)
                    {
                        _logger.LogWarning(
                            "Projekcja sesji {Session}: konflikt wersji po {Attempts} próbach, pomijam zdarzenie.",
                            sessionId, MaxConcurrencyAttempts);
                        return;
                    }
                }
            }
        }, ct);
    }

    /// <summary>Scala pojedyncze zdarzenie w dokument sesji (idempotentnie poza commandCount).</summary>
    private static void Merge(SessionProjection doc, HoneypotEvent evt)
    {
        if (string.IsNullOrEmpty(doc.AttackerIp) && !string.IsNullOrEmpty(evt.AttackerIp))
        {
            doc.AttackerIp = evt.AttackerIp;
        }

        if (string.IsNullOrEmpty(doc.SensorId) && !string.IsNullOrEmpty(evt.SensorId))
        {
            doc.SensorId = evt.SensorId;
        }

        if (evt.Timestamp < doc.StartedAt)
        {
            doc.StartedAt = evt.Timestamp;
        }

        if (evt.Timestamp > doc.EndedAt)
        {
            doc.EndedAt = evt.Timestamp;
        }

        var duration = (doc.EndedAt - doc.StartedAt).TotalMilliseconds;
        doc.DurationMs = duration > 0 ? (long)duration : 0;

        if (evt.EventType == EventType.Command)
        {
            doc.CommandCount++;
        }

        if (!string.IsNullOrWhiteSpace(evt.TtyRef))
        {
            doc.TtyRef = evt.TtyRef;
            doc.HasTty = true;
        }

        if (!string.IsNullOrWhiteSpace(evt.Geo?.Country))
        {
            doc.Country = evt.Geo.Country!;
            doc.CountryName = evt.Geo.CountryName ?? doc.CountryName;
        }

        doc.LastEventType = evt.EventType.ToString();
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
                UseSystemTextJsonSerializerWithOptions = HoneyGridJson.Options,
            });

        _container = _client.GetContainer(_options.CosmosDatabase, _options.CosmosSessionsContainer);

        _logger.LogInformation(
            "CosmosSessionWriter połączony bezkluczowo z {Endpoint} ({Db}/{Container}).",
            _options.CosmosEndpoint, _options.CosmosDatabase, _options.CosmosSessionsContainer);

        return _container;
    }

    public void Dispose() => _client?.Dispose();

    /// <summary>
    /// Dokument sesji w Cosmos. Serializowany przez <see cref="HoneyGridJson.Options"/>
    /// (camelCase → "id"/"sessionId"), zgodny z odczytem w SessionEndpoints (replay + lista).
    /// </summary>
    internal sealed class SessionProjection
    {
        public string Id { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string AttackerIp { get; set; } = string.Empty;
        public string SensorId { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset EndedAt { get; set; }
        public long DurationMs { get; set; }
        public int CommandCount { get; set; }
        public string? TtyRef { get; set; }
        public bool HasTty { get; set; }
        public string Country { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public string LastEventType { get; set; } = string.Empty;
        public string DocType { get; set; } = "session";
    }
}
