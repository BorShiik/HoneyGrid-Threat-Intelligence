using System.Diagnostics;
using System.Threading.Channels;
using Azure;
using Azure.Core.Serialization;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Czwarty sink fan-outu: wysyłka wzbogaconych zdarzeń do Microsoft Sentinel
/// przez Logs Ingestion API (DCE + DCR) do niestandardowej tabeli Cowrie_CL.
///
/// Architektura identyczna jak <see cref="ServiceBusForwarder"/>: sink jest
/// jednocześnie BackgroundService (pętla flush w tle) i IEventSinkTarget
/// (WriteAsync tylko odkłada do bufora — fan-out workera nie czeka na Sentinel).
/// Zdarzenia buforujemy w Channel i wysyłamy paczką, gdy zbierze się
/// SentinelBatchSize wierszy LUB upłynie SentinelFlushIntervalMs (BatchTrigger).
///
/// Sink jest OPCJONALNY: bez kompletnej pary DCE/DCR po prostu drenuje i porzuca
/// zdarzenia. Sentinel to druga ścieżka telemetrii — źródłem prawdy pozostaje
/// Cosmos/Blob, dlatego sink NIGDY nie wywraca procesu.
/// Klient bezkluczowy (DefaultAzureCredential), tworzony leniwie przy pierwszej wysyłce.
///
/// SERIALIZACJA: używamy przeciążenia UploadAsync(ruleId, streamName, IEnumerable&lt;object&gt;,
/// LogsUploadOptions) z JAWNYM JsonObjectSerializer opartym na
/// <see cref="CowrieRowMapper.SerializerOptions"/> (PascalCase 1:1). Gwarantuje to
/// dokładne nazwy kluczy zgodne z deklaracją strumienia DCR, a jednocześnie zachowuje
/// wbudowane w SDK dzielenie na porcje (limit 1 MB) i kompresję gzip.
/// </summary>
public sealed class SentinelLogsSink : BackgroundService, IEventSinkTarget
{
    private readonly IngestionOptions _options;
    private readonly ILogger<SentinelLogsSink> _logger;
    private readonly Channel<HoneypotEvent> _channel;
    private readonly BatchTrigger _trigger;
    private readonly ResiliencePipeline _retry;
    private readonly LogsUploadOptions _uploadOptions;
    private LogsIngestionClient? _client;

    public SentinelLogsSink(IOptions<IngestionOptions> options, ILogger<SentinelLogsSink> logger)
    {
        _options = options.Value;
        _logger = logger;

        _trigger = new BatchTrigger(
            _options.SentinelBatchSize,
            TimeSpan.FromMilliseconds(_options.SentinelFlushIntervalMs));

        // Bufor ograniczony: przy przepełnieniu wyrzucamy najstarsze zdarzenia
        // (Sentinel jest "best effort" — Cosmos/Blob pozostają źródłem prawdy).
        _channel = Channel.CreateBounded<HoneypotEvent>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        // Polly: ponawiamy throttling (429) i błędy serwera (>=500) Logs Ingestion API.
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<RequestFailedException>(ex => ex.Status == 429 || ex.Status >= 500)
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 4,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();

        // Jawny serializator PascalCase — patrz komentarz klasy (kontrakt strumienia DCR).
        _uploadOptions = new LogsUploadOptions
        {
            Serializer = new JsonObjectSerializer(CowrieRowMapper.SerializerOptions),
        };
    }

    public string Name => "SentinelLogs";

    /// <summary>
    /// Czy sink jest skonfigurowany: wymagane są OBA pola DCE + DCR
    /// (walidator opcji pilnuje, by nie ustawiono tylko jednego z nich).
    /// </summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_options.DceLogsIngestionEndpoint) &&
        !string.IsNullOrWhiteSpace(_options.DcrImmutableId);

    /// <summary>Fan-out workera: tylko odkłada zdarzenie do bufora (nieblokujące).</summary>
    public ValueTask WriteAsync(HoneypotEvent evt, CancellationToken ct)
    {
        if (!IsEnabled || _options.DryRun)
        {
            return ValueTask.CompletedTask; // sink wyłączony — świadomy no-op
        }

        if (!_channel.Writer.TryWrite(evt))
        {
            _logger.LogWarning("Bufor Sentinel pełny — zdarzenie {Id} pominięte.", evt.Id);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Pętla flush: zbiera zdarzenia i wysyła paczki wg BatchTrigger.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.DryRun || !IsEnabled)
        {
            _logger.LogInformation(
                "SentinelLogsSink wyłączony (DryRun lub brak Ingestion__DceLogsIngestionEndpoint / " +
                "Ingestion__DcrImmutableId) — wysyłka do Sentinela pominięta.");
            await DrainAndDiscardAsync(stoppingToken);
            return;
        }

        var buffer = new List<HoneypotEvent>(_options.SentinelBatchSize);
        var stopwatch = Stopwatch.StartNew();
        var reader = _channel.Reader;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Czekamy na zdarzenia maksymalnie do końca bieżącego okna flush.
                var remaining = _trigger.FlushInterval - stopwatch.Elapsed;

                if (remaining > TimeSpan.Zero && buffer.Count < _trigger.MaxBatchSize)
                {
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    waitCts.CancelAfter(remaining);

                    try
                    {
                        if (!await reader.WaitToReadAsync(waitCts.Token))
                        {
                            break; // kanał zamknięty
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Upłynęło okno flush — sprawdzimy trigger poniżej.
                    }
                }

                while (buffer.Count < _trigger.MaxBatchSize && reader.TryRead(out var evt))
                {
                    buffer.Add(evt);
                }

                if (_trigger.ShouldFlush(buffer.Count, stopwatch.Elapsed))
                {
                    await UploadBatchSafelyAsync(buffer, stoppingToken);
                    buffer.Clear();
                    stopwatch.Restart();
                }
                else if (buffer.Count == 0)
                {
                    stopwatch.Restart(); // pusty bufor — okno liczymy od nowa
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Zamykanie hosta — drenaż poniżej.
        }

        // Drenaż przy zamykaniu: wysyłamy co się da, bez tokenu anulowania.
        while (reader.TryRead(out var evt))
        {
            buffer.Add(evt);
            if (buffer.Count >= _trigger.MaxBatchSize)
            {
                await UploadBatchSafelyAsync(buffer, CancellationToken.None);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await UploadBatchSafelyAsync(buffer, CancellationToken.None);
        }
    }

    /// <summary>Sink wyłączony: czytamy i porzucamy zdarzenia, żeby bufor nie pęczniał.</summary>
    private async Task DrainAndDiscardAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _channel.Reader.ReadAllAsync(ct))
            {
                // celowo puste
            }
        }
        catch (OperationCanceledException)
        {
            // normalne zamknięcie
        }
    }

    /// <summary>
    /// Wysyłka paczki z ponawianiem; błąd ostateczny jest logowany, a paczka porzucana —
    /// Sentinel to druga ścieżka telemetrii, źródłem prawdy pozostaje Cosmos/Blob,
    /// więc sink NIGDY nie wywraca procesu (np. 403 w czasie propagacji RBAC po deployu).
    /// </summary>
    private async Task UploadBatchSafelyAsync(IReadOnlyList<HoneypotEvent> events, CancellationToken ct)
    {
        if (events.Count == 0)
        {
            return;
        }

        try
        {
            var client = GetClient();

            // Spłaszczamy do wierszy Cowrie_CL PRZED wysyłką (czysta, testowalna logika).
            var rows = new List<CowrieRow>(events.Count);
            foreach (var evt in events)
            {
                rows.Add(CowrieRowMapper.ToRow(evt));
            }

            await _retry.ExecuteAsync(async token =>
            {
                await client.UploadAsync(
                    _options.DcrImmutableId,
                    _options.DcrStreamName,
                    rows,
                    _uploadOptions,
                    token);
            }, ct);

            _logger.LogDebug(
                "Wysłano paczkę {Count} wierszy do strumienia {Stream} (Sentinel).",
                rows.Count, _options.DcrStreamName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Nie udało się wysłać paczki {Count} zdarzeń do Sentinela — paczka porzucona " +
                "(druga ścieżka telemetrii; źródłem prawdy pozostaje Cosmos/Blob).",
                events.Count);
        }
    }

    /// <summary>Leniwa konstrukcja klienta Logs Ingestion (DCE + DefaultAzureCredential).</summary>
    private LogsIngestionClient GetClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        _client = new LogsIngestionClient(
            new Uri(_options.DceLogsIngestionEndpoint),
            new DefaultAzureCredential());

        _logger.LogInformation(
            "SentinelLogsSink połączony bezkluczowo z {Endpoint} (DCR {DcrId}, strumień {Stream}).",
            _options.DceLogsIngestionEndpoint, _options.DcrImmutableId, _options.DcrStreamName);

        return _client;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }
}
