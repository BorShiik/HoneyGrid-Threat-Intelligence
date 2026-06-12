using HoneyGrid.Contracts;

namespace HoneyGrid.Ingestion.Enrichment;

/// <summary>
/// Pojedynczy krok pipeline'u wzbogacania zdarzeń.
/// <see cref="HoneypotEvent"/> jest niemutowalnym rekordem (settery init-only),
/// dlatego enricher zwraca wzbogaconą kopię (<c>evt with { ... }</c>);
/// gdy nie ma nic do dodania — zwraca wejściowe zdarzenie bez zmian.
/// KONTRAKT ODPORNOŚCI: enricher NIGDY nie może rzucić wyjątku, który ubije
/// przetwarzanie zdarzenia — błędy wzbogacania degradują się do braku wzbogacenia.
/// </summary>
public interface IEventEnricher
{
    /// <summary>Wzbogaca zdarzenie; zwraca kopię lub wejściowy obiekt bez zmian.</summary>
    ValueTask<HoneypotEvent> EnrichAsync(HoneypotEvent evt, CancellationToken ct);
}
