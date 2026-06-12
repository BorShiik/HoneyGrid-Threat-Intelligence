using HoneyGrid.Contracts;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Ingestion.Enrichment;

/// <summary>
/// Składa zarejestrowane enrichery w jeden pipeline: zdarzenie przechodzi przez
/// nie w kolejności rejestracji w DI, a wynik każdego kroku jest wejściem następnego
/// (fold po niemutowalnym rekordzie). Dodatkowa siatka bezpieczeństwa: wyjątek
/// pojedynczego enrichera jest logowany, a pipeline kontynuuje z poprzednią wersją
/// zdarzenia — wzbogacanie nigdy nie blokuje ingestii.
/// </summary>
public sealed class EnrichmentPipeline(
    IEnumerable<IEventEnricher> enrichers,
    ILogger<EnrichmentPipeline> logger)
{
    private readonly IReadOnlyList<IEventEnricher> _enrichers = enrichers.ToList();

    /// <summary>Przepuszcza zdarzenie przez wszystkie enrichery po kolei.</summary>
    public async ValueTask<HoneypotEvent> EnrichAsync(HoneypotEvent evt, CancellationToken ct)
    {
        var current = evt;

        foreach (var enricher in _enrichers)
        {
            try
            {
                current = await enricher.EnrichAsync(current, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // zamykanie hosta — propagujemy
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Enricher {Enricher} zgłosił błąd dla zdarzenia {EventId} — pomijam ten krok wzbogacania.",
                    enricher.GetType().Name,
                    evt.Id);
            }
        }

        return current;
    }
}
