using HoneyGrid.Contracts;
using HoneyGrid.Functions.Aggregation;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Predrachowanie agregatów dashboardu (Track B, Tydzień 4).
///
/// Wyzwalacz czasowy (domyślnie co 5 min). Czyta zdarzenia z okna analizy,
/// liczy agregaty overview/geo/credentials (<see cref="AggregateBuilder"/>) i
/// zapisuje po jednym dokumencie na widok do kontenera <c>aggregates</c>
/// (PK <c>/bucket</c>). API odczytuje gotowe dokumenty zamiast liczyć na żywo —
/// niższe RU i szybsze odpowiedzi.
/// </summary>
public sealed class BuildAggregates
{
    // Okno analizy dla agregatów (overview liczy też totalEvents w tym oknie).
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private readonly CosmosClient _cosmos;
    private readonly ILogger<BuildAggregates> _logger;
    private readonly string _databaseName;

    public BuildAggregates(CosmosClient cosmos, IConfiguration config, ILogger<BuildAggregates> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
    }

    [Function(nameof(BuildAggregates))]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var events = await LoadRecentEventsAsync(cancellationToken);
        if (events.Count == 0)
        {
            _logger.LogInformation("Agregaty: brak zdarzeń w oknie {Days} dni.", Window.TotalDays);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var container = _cosmos.GetContainer(_databaseName, "aggregates");

        var overview = AggregateBuilder.BuildOverview(events, now);
        var geo = AggregateBuilder.BuildGeo(events, now);
        var credentials = AggregateBuilder.BuildCredentials(events, now);

        await UpsertAsync(container, overview, overview.Bucket, cancellationToken);
        await UpsertAsync(container, geo, geo.Bucket, cancellationToken);
        await UpsertAsync(container, credentials, credentials.Bucket, cancellationToken);

        _logger.LogInformation(
            "Agregaty zaktualizowane: {Events} zdarzeń → {Countries} krajów, {Geo} punktów geo, {Pairs} par poświadczeń.",
            events.Count, overview.TopCountries.Count, geo.Points.Count, credentials.TopPairs.Count);
    }

    private async Task UpsertAsync<T>(Container container, T document, string bucket, CancellationToken ct)
    {
        try
        {
            await container.UpsertItemAsync(document, new PartitionKey(bucket), cancellationToken: ct);
        }
        catch (CosmosException ex)
        {
            _logger.LogWarning(ex, "Agregaty: upsert '{Bucket}' nieudany (status {Status}).", bucket, ex.StatusCode);
        }
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
            _logger.LogWarning(ex, "Agregaty: odczyt zdarzeń z Cosmos nieudany.");
        }

        return events;
    }
}
