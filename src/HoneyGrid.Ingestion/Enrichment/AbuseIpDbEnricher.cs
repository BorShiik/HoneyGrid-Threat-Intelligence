using System.Net;
using System.Text.Json;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HoneyGrid.Ingestion.Enrichment;

/// <summary>
/// Wzbogacanie o reputację IP z AbuseIPDB (https://api.abuseipdb.com/api/v2/check).
///
/// Aktywne tylko gdy skonfigurowano klucz API (Ingestion:AbuseIpDbApiKey) — bez klucza
/// enricher loguje jedną informację przy starcie i jest no-opem.
/// Mapowanie: knownMalicious = abuseConfidenceScore >= 50, score = surowy wynik 0–100,
/// sources = ["AbuseIPDB"]. Wyniki cache'owane per IP przez 6 h (oszczędność limitu API).
/// Każdy błąd (sieć, 429, niepoprawny JSON) => zdarzenie płynie dalej bez threatIntel.
/// </summary>
public sealed class AbuseIpDbEnricher : IEventEnricher
{
    /// <summary>Nazwa nazwanego klienta HTTP w IHttpClientFactory.</summary>
    public const string HttpClientName = "abuseipdb";

    /// <summary>Próg wyniku, od którego IP uznajemy za znane-złośliwe.</summary>
    public const int MaliciousScoreThreshold = 50;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AbuseIpDbEnricher> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _retry;

    public AbuseIpDbEnricher(
        IHttpClientFactory httpClientFactory,
        IOptions<IngestionOptions> options,
        IMemoryCache cache,
        ILogger<AbuseIpDbEnricher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _cache = cache;
        _logger = logger;

        if (!IsEnabled)
        {
            _logger.LogInformation(
                "Brak klucza Ingestion:AbuseIpDbApiKey — wzbogacanie AbuseIPDB wyłączone (no-op).");
        }

        // Polly: 3 ponowienia z backoffem wykładniczym na błędy sieci i 429/5xx.
        _retry = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests
                                       || (int)r.StatusCode >= 500),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(250),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();
    }

    /// <summary>Czy enricher jest aktywny (skonfigurowany klucz API).</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.AbuseIpDbApiKey);

    public async ValueTask<HoneypotEvent> EnrichAsync(HoneypotEvent evt, CancellationToken ct)
    {
        // No-op: brak klucza albo zdarzenie już ma threatIntel (idempotencja).
        if (!IsEnabled || evt.ThreatIntel is not null)
        {
            return evt;
        }

        var cacheKey = $"abuse:{evt.AttackerIp}";

        if (!_cache.TryGetValue(cacheKey, out ThreatIntelInfo? intel))
        {
            intel = await QuerySafelyAsync(evt.AttackerIp, ct);

            if (intel is not null)
            {
                // Cache'ujemy tylko udane odpowiedzi — błędy mają szansę przy kolejnym zdarzeniu.
                using var entry = _cache.CreateEntry(cacheKey);
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                entry.Size = 1;
                entry.Value = intel;
            }
        }

        return intel is null ? evt : evt with { ThreatIntel = intel };
    }

    /// <summary>Zapytanie do API z ponawianiem; każdy błąd => null (bez wzbogacenia).</summary>
    private async Task<ThreatIntelInfo?> QuerySafelyAsync(string ip, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var response = await _retry.ExecuteAsync(
                async token =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        $"https://api.abuseipdb.com/api/v2/check?ipAddress={Uri.EscapeDataString(ip)}&maxAgeInDays=90");
                    request.Headers.Add("Key", _options.AbuseIpDbApiKey);
                    request.Headers.Add("Accept", "application/json");
                    return await client.SendAsync(request, token);
                },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AbuseIPDB zwróciło status {Status} dla {Ip} — pomijam wzbogacenie.", (int)response.StatusCode, ip);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var score = doc.RootElement
                .GetProperty("data")
                .GetProperty("abuseConfidenceScore")
                .GetInt32();

            return new ThreatIntelInfo
            {
                KnownMalicious = score >= MaliciousScoreThreshold,
                Score = score,
                Sources = ["AbuseIPDB"],
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // zamykanie hosta
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Zapytanie AbuseIPDB dla {Ip} nie powiodło się — pomijam wzbogacenie.", ip);
            return null;
        }
    }
}
