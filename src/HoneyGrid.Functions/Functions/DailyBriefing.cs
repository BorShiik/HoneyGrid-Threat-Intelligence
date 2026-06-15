using System.Text.Json.Serialization;
using HoneyGrid.Contracts;
using HoneyGrid.Functions.Aggregation;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Dobowy briefing bezpieczeństwa (Track B). Codziennie o 06:00 UTC podsumowuje
/// aktywność ostatnich 24 h (wolumen, unikalni atakujący, top kraj/sensor/typ)
/// i zapisuje narrację do kontenera <c>aggregates</c> (bucket <c>briefing</c>),
/// skąd dashboard może ją pokazać. Loguje też do App Insights.
///
/// Podsumowanie jest deterministyczne (czytelne i niezawodne na demo). Rozszerzenie
/// docelowe: narracja przez Azure OpenAI + wysyłka e-mail przez Communication
/// Services / Logic Apps (wymaga dodatkowego zasobu ACS i domeny e-mail).
/// </summary>
public sealed class DailyBriefing
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly CosmosClient _cosmos;
    private readonly ILogger<DailyBriefing> _logger;
    private readonly string _databaseName;

    public DailyBriefing(CosmosClient cosmos, IConfiguration config, ILogger<DailyBriefing> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
    }

    [Function(nameof(DailyBriefing))]
    public async Task Run(
        [TimerTrigger("0 0 6 * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var events = await LoadRecentEventsAsync(cancellationToken);
        var overview = AggregateBuilder.BuildOverview(events, now);

        var topCountry = overview.TopCountries.Count > 0 ? overview.TopCountries[0] : null;
        var topSensor = overview.EventsBySensorType.Count > 0
            ? overview.EventsBySensorType.OrderByDescending(kv => kv.Value).First().Key
            : "—";
        var topType = overview.EventsByType.Count > 0
            ? overview.EventsByType.OrderByDescending(kv => kv.Value).First().Key
            : "—";

        var summary =
            $"Dobowy briefing HoneyGrid ({now:yyyy-MM-dd}). " +
            $"W ostatnich 24 h zarejestrowano {overview.EventsLast24h} zdarzeń od " +
            $"{overview.UniqueAttackers} unikalnych adresów IP. " +
            (topCountry is not null
                ? $"Najaktywniejszy kraj: {topCountry.CountryName} ({topCountry.Country}) — {topCountry.Count} zdarzeń. "
                : "") +
            $"Dominujący sensor: {topSensor}; najczęstszy typ zdarzenia: {topType}. " +
            $"Aktywnych sesji (ostatnie 5 min): {overview.ActiveSessions}.";

        var doc = new BriefingDocument
        {
            Date = now.ToString("yyyy-MM-dd"),
            GeneratedAt = now,
            Summary = summary,
            EventsLast24h = overview.EventsLast24h,
            UniqueAttackers = overview.UniqueAttackers,
            TopCountry = topCountry?.Country,
        };

        try
        {
            var container = _cosmos.GetContainer(_databaseName, "aggregates");
            await container.UpsertItemAsync(doc, new PartitionKey(doc.Bucket), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex)
        {
            _logger.LogWarning(ex, "Briefing: zapis do Cosmos nieudany (status {Status}).", ex.StatusCode);
        }

        _logger.LogInformation("Dobowy briefing: {Summary}", summary);
    }

    private async Task<List<HoneypotEvent>> LoadRecentEventsAsync(CancellationToken ct)
    {
        var events = new List<HoneypotEvent>();
        var cutoff = DateTimeOffset.UtcNow.Subtract(Window).ToString("O");

        try
        {
            var container = _cosmos.GetContainer(_databaseName, "events");
            var query = new QueryDefinition("SELECT * FROM c WHERE c.timestamp > @cutoff")
                .WithParameter("@cutoff", cutoff);
            using var iterator = container.GetItemQueryIterator<HoneypotEvent>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                events.AddRange(page);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Briefing: odczyt zdarzeń z Cosmos nieudany.");
        }

        return events;
    }
}

/// <summary>Dokument briefingu w kontenerze <c>aggregates</c> (PK <c>/bucket</c>).</summary>
public sealed record BriefingDocument
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "briefing";

    [JsonPropertyName("bucket")]
    public string Bucket { get; init; } = "briefing";

    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("generatedAt")]
    public required DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("eventsLast24h")]
    public long EventsLast24h { get; init; }

    [JsonPropertyName("uniqueAttackers")]
    public long UniqueAttackers { get; init; }

    [JsonPropertyName("topCountry")]
    public string? TopCountry { get; init; }
}
