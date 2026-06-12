using System.Diagnostics;
using System.Threading.Channels;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Przekazywanie wzbogaconych zdarzeń do kolejki Service Bus "ai-classify" (Track B).
///
/// EKONOMIA: klasyfikator AI Track B przetwarza po ~50 zdarzeń na jedno wywołanie
/// OpenAI, dlatego buforujemy zdarzenia w Channel i wysyłamy paczką
/// (ServiceBusMessageBatch), gdy zbierze się ServiceBusBatchSize zdarzeń
/// LUB upłynie ServiceBusFlushIntervalMs (logika w <see cref="BatchTrigger"/>).
///
/// Sink jest jednocześnie BackgroundService (pętla flush w tle) i IEventSinkTarget
/// (WriteAsync tylko odkłada do bufora — fan-out workera nie czeka na Service Bus).
/// Klient bezkluczowy (DefaultAzureCredential), tworzony leniwie przy pierwszej wysyłce.
/// </summary>
public sealed class ServiceBusForwarder : BackgroundService, IEventSinkTarget
{
    private readonly IngestionOptions _options;
    private readonly ILogger<ServiceBusForwarder> _logger;
    private readonly Channel<HoneypotEvent> _channel;
    private readonly BatchTrigger _trigger;
    private readonly ResiliencePipeline _retry;
    private ServiceBusClient? _client;
    private ServiceBusSender? _sender;

    public ServiceBusForwarder(IOptions<IngestionOptions> options, ILogger<ServiceBusForwarder> logger)
    {
        _options = options.Value;
        _logger = logger;

        _trigger = new BatchTrigger(
            _options.ServiceBusBatchSize,
            TimeSpan.FromMilliseconds(_options.ServiceBusFlushIntervalMs));

        // Bufor ograniczony: przy przepełnieniu wyrzucamy najstarsze zdarzenia
        // (kolejka AI jest "best effort" — Cosmos/Blob pozostają źródłem prawdy).
        _channel = Channel.CreateBounded<HoneypotEvent>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        // Polly: ponawiamy przejściowe błędy AMQP Service Bus.
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<ServiceBusException>(ex => ex.IsTransient)
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 4,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();
    }

    public string Name => "ServiceBus";

    /// <summary>Fan-out workera: tylko odkłada zdarzenie do bufora (nieblokujące).</summary>
    public ValueTask WriteAsync(HoneypotEvent evt, CancellationToken ct)
    {
        if (!_channel.Writer.TryWrite(evt))
        {
            _logger.LogWarning("Bufor Service Bus pełny — zdarzenie {Id} pominięte.", evt.Id);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Pętla flush: zbiera zdarzenia i wysyła paczki wg BatchTrigger.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.DryRun)
        {
            _logger.LogInformation("ServiceBusForwarder w trybie DryRun — wysyłka wyłączona.");
            await DrainAndDiscardAsync(stoppingToken);
            return;
        }

        var buffer = new List<HoneypotEvent>(_options.ServiceBusBatchSize);
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
                    await SendBatchSafelyAsync(buffer, stoppingToken);
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
                await SendBatchSafelyAsync(buffer, CancellationToken.None);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await SendBatchSafelyAsync(buffer, CancellationToken.None);
        }
    }

    /// <summary>DryRun: czytamy i porzucamy zdarzenia, żeby bufor nie pęczniał.</summary>
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
    /// forwarder NIGDY nie wywraca procesu (np. 403 w czasie propagacji RBAC po deployu).
    /// </summary>
    private async Task SendBatchSafelyAsync(IReadOnlyList<HoneypotEvent> events, CancellationToken ct)
    {
        if (events.Count == 0)
        {
            return;
        }

        try
        {
            var sender = GetSender();

            await _retry.ExecuteAsync(async token =>
            {
                using var batch = await sender.CreateMessageBatchAsync(token);

                foreach (var evt in events)
                {
                    var message = new ServiceBusMessage(HoneyGridJson.Serialize(evt))
                    {
                        ContentType = "application/json",
                        MessageId = evt.Id.ToString(),
                    };

                    if (!batch.TryAddMessage(message))
                    {
                        _logger.LogWarning("Zdarzenie {Id} nie mieści się w paczce Service Bus — pominięte.", evt.Id);
                    }
                }

                if (batch.Count > 0)
                {
                    await sender.SendMessagesAsync(batch, token);
                }
            }, ct);

            _logger.LogDebug("Wysłano paczkę {Count} zdarzeń do kolejki {Queue}.", events.Count, _options.ServiceBusQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Nie udało się wysłać paczki {Count} zdarzeń do Service Bus — paczka porzucona (kolejka AI jest best-effort).",
                events.Count);
        }
    }

    /// <summary>Leniwa konstrukcja klienta i sendera (FQDN + DefaultAzureCredential).</summary>
    private ServiceBusSender GetSender()
    {
        if (_sender is not null)
        {
            return _sender;
        }

        _client = new ServiceBusClient(
            _options.ServiceBusFullyQualifiedNamespace,
            new DefaultAzureCredential());
        _sender = _client.CreateSender(_options.ServiceBusQueue);

        _logger.LogInformation(
            "ServiceBusForwarder połączony bezkluczowo z {Namespace}/{Queue}.",
            _options.ServiceBusFullyQualifiedNamespace, _options.ServiceBusQueue);

        return _sender;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);

        if (_sender is not null)
        {
            await _sender.DisposeAsync();
        }

        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
