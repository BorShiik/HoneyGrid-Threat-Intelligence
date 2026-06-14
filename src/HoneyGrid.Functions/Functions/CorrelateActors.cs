using HoneyGrid.Contracts;
using HoneyGrid.Functions.Profiling;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Profilowanie aktorów zagrożeń (Track B, killer-ficzy ③).
///
/// Wyzwalacz czasowy (domyślnie co 30 min). Czyta zdarzenia z okna retencji,
/// buduje odciski per IP (<see cref="FingerprintBuilder"/>), koreluje je w
/// aktorów (<see cref="ActorClustering"/>) i zapisuje profile z dossier
/// (<see cref="ActorProfileBuilder"/>) do kontenera Cosmos <c>actors</c>.
///
/// Dossier jest na razie heurystyczne (deterministyczne). W Tygodniu 6 można
/// podmienić opis/archetyp na generowany przez Azure OpenAI — heurystyka zostaje
/// jako fallback przy błędzie/limicie modelu.
/// </summary>
public sealed class CorrelateActors
{
    // Okno analizy — ile wstecz czytamy zdarzenia do korelacji.
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private readonly CosmosClient _cosmos;
    private readonly ILogger<CorrelateActors> _logger;
    private readonly string _databaseName;

    public CorrelateActors(CosmosClient cosmos, IConfiguration config, ILogger<CorrelateActors> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
    }

    [Function(nameof(CorrelateActors))]
    public async Task Run(
        [TimerTrigger("0 */30 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var events = await LoadRecentEventsAsync(cancellationToken);
        if (events.Count == 0)
        {
            _logger.LogInformation("Korelacja aktorów: brak zdarzeń w oknie {Days} dni.", Window.TotalDays);
            return;
        }

        var fingerprints = FingerprintBuilder.FromEvents(events);
        var clusters = ActorClustering.Cluster(fingerprints);

        var actorsContainer = _cosmos.GetContainer(_databaseName, "actors");
        var upserted = 0;

        foreach (var cluster in clusters)
        {
            var profile = ActorProfileBuilder.Build(cluster);
            try
            {
                await actorsContainer.UpsertItemAsync(profile, new PartitionKey(profile.Id), cancellationToken: cancellationToken);
                upserted++;
            }
            catch (CosmosException ex)
            {
                _logger.LogWarning(ex, "Korelacja: upsert aktora {ActorId} nieudany (status {Status}).",
                    profile.Id, ex.StatusCode);
            }
        }

        _logger.LogInformation(
            "Korelacja aktorów: {Events} zdarzeń → {Fingerprints} źródeł → {Actors} aktorów (zapisano {Upserted}).",
            events.Count, fingerprints.Count, clusters.Count, upserted);
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
            _logger.LogWarning(ex, "Korelacja: odczyt zdarzeń z Cosmos nieudany.");
        }

        return events;
    }
}
