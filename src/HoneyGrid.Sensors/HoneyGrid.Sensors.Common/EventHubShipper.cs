using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Sensors.Common;

/// <summary>
/// Serwis w tle wysyłający zdarzenia honeypota do Azure Event Hubs.
///
/// Architektura bezkluczowa (keyless): <see cref="EventHubProducerClient"/> tworzony jest
/// konstruktorem z w pełni kwalifikowaną przestrzenią nazw (FQDN) + <see cref="DefaultAzureCredential"/>.
/// Lokalnie uwierzytelnia poprzez <c>az login</c> / Visual Studio, w chmurze poprzez Managed Identity.
/// Brak connection stringów i kluczy współdzielonych.
///
/// Działanie: czyta zdarzenia ze współdzielonego <see cref="HoneypotEventChannel"/>, buduje
/// <see cref="EventDataBatch"/> i wysyła paczkę gdy (a) osiągnie maksymalny rozmiar lub
/// (b) upłynie interwał flush. Transientne błędy AMQP są ponawiane przez Polly (backoff wykładniczy).
///
/// Tryb lokalny (<see cref="SensorOptions.LocalLogOnly"/> = true): pomija Azure całkowicie —
/// każde zdarzenie jest jedynie logowane, dzięki czemu cały system działa bez subskrypcji Azure.
/// </summary>
public sealed class EventHubShipper : BackgroundService
{
    private readonly HoneypotEventChannel _channel;
    private readonly SensorOptions _options;
    private readonly ILogger<EventHubShipper> _logger;
    private readonly ResiliencePipeline _retryPipeline;
    private EventHubProducerClient? _producer;

    public EventHubShipper(
        HoneypotEventChannel channel,
        IOptions<SensorOptions> options,
        ILogger<EventHubShipper> logger)
    {
        _channel = channel;
        _options = options.Value;
        _logger = logger;

        // Polly: ponawianie transientnych błędów AMQP (np. chwilowa utrata połączenia z Event Hub).
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<EventHubsException>(ex => ex.IsTransient)
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 4,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Ponawiam wysyłkę do Event Hub (próba {Attempt}) po błędzie: {Error}",
                        args.AttemptNumber + 1,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.LocalLogOnly)
        {
            _logger.LogInformation(
                "EventHubShipper w trybie LOKALNYM (LocalLogOnly=true) — zdarzenia będą tylko logowane, bez wysyłki do Azure.");
            await RunLocalLogLoopAsync(stoppingToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.EventHubFullyQualifiedNamespace))
        {
            throw new InvalidOperationException(
                "Brak EventHubFullyQualifiedNamespace przy LocalLogOnly=false. " +
                "Ustaw przestrzeń nazw Event Hubs lub włącz tryb lokalny.");
        }

        // Bezkluczowe utworzenie producenta: FQDN + DefaultAzureCredential.
        _producer = new EventHubProducerClient(
            _options.EventHubFullyQualifiedNamespace,
            _options.EventHubName,
            new DefaultAzureCredential());

        _logger.LogInformation(
            "EventHubShipper połączony bezkluczowo z {Namespace}/{Hub} (DefaultAzureCredential).",
            _options.EventHubFullyQualifiedNamespace,
            _options.EventHubName);

        await RunBatchLoopAsync(stoppingToken);
    }

    /// <summary>Pętla wyłącznie logująca — używana w trybie lokalnym (bez Azure).</summary>
    private async Task RunLocalLogLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("[LOKALNE] Zdarzenie honeypota: {Event}", HoneyGridJson.Serialize(evt));
            }
        }
        catch (OperationCanceledException)
        {
            // Normalne zamknięcie aplikacji.
        }
    }

    /// <summary>Pętla batchująca i wysyłająca paczki do Event Hub.</summary>
    private async Task RunBatchLoopAsync(CancellationToken stoppingToken)
    {
        var flushInterval = TimeSpan.FromMilliseconds(_options.FlushIntervalMs);
        var buffer = new List<HoneypotEvent>(_options.MaxBatchSize);
        var reader = _channel.Reader;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                buffer.Clear();

                // Czekamy (z limitem czasu = interwał flush) na pierwsze zdarzenie.
                using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                flushCts.CancelAfter(flushInterval);

                try
                {
                    if (!await reader.WaitToReadAsync(flushCts.Token))
                    {
                        break; // kanał zamknięty
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    continue; // upłynął interwał flush bez zdarzeń — pętla od nowa
                }

                // Zbieramy do MaxBatchSize zdarzeń, które są już dostępne.
                while (buffer.Count < _options.MaxBatchSize && reader.TryRead(out var evt))
                {
                    buffer.Add(evt);
                }

                if (buffer.Count > 0)
                {
                    await SendBatchAsync(buffer, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Zamykanie — wyślemy resztę poniżej.
        }

        // Drenaż: wysyłka pozostałych zdarzeń przy zamykaniu.
        buffer.Clear();
        while (reader.TryRead(out var evt))
        {
            buffer.Add(evt);
            if (buffer.Count >= _options.MaxBatchSize)
            {
                await SendBatchAsync(buffer, CancellationToken.None);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await SendBatchAsync(buffer, CancellationToken.None);
        }
    }

    /// <summary>Buduje EventDataBatch i wysyła z ponawianiem Polly.</summary>
    private async Task SendBatchAsync(IReadOnlyList<HoneypotEvent> events, CancellationToken cancellationToken)
    {
        if (_producer is null || events.Count == 0)
        {
            return;
        }

        await _retryPipeline.ExecuteAsync(async ct =>
        {
            var batch = await _producer.CreateBatchAsync(ct);

            try
            {
                foreach (var evt in events)
                {
                    var data = new EventData(HoneyGridJson.Serialize(evt));

                    if (batch.TryAdd(data))
                    {
                        continue;
                    }

                    // Nie zmieściło się. Jeśli paczka jest pusta — pojedyncze zdarzenie jest za duże, pomijamy.
                    if (batch.Count == 0)
                    {
                        _logger.LogWarning("Zdarzenie {Id} przekracza limit rozmiaru paczki — pominięto.", evt.Id);
                        continue;
                    }

                    // Paczka pełna — wysyłamy bieżącą i otwieramy nową dla tego zdarzenia.
                    await _producer.SendAsync(batch, ct);
                    batch.Dispose();
                    batch = await _producer.CreateBatchAsync(ct);
                    batch.TryAdd(data);
                }

                if (batch.Count > 0)
                {
                    await _producer.SendAsync(batch, ct);
                }
            }
            finally
            {
                batch.Dispose();
            }
        }, cancellationToken);

        _logger.LogDebug("Wysłano paczkę {Count} zdarzeń do Event Hub.", events.Count);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_producer is not null)
        {
            await _producer.DisposeAsync();
        }
    }
}
