using HoneyGrid.Contracts;

namespace HoneyGrid.Functions.Classification;

/// <summary>
/// Klasyfikator zastępczy (stub) — Tydzień 3.
///
/// Zwraca <see cref="ClassificationInfo"/> w tym samym kształcie, co docelowy
/// klasyfikator AI (Azure OpenAI, Tydzień 5), ale na podstawie prostego mapowania
/// po typie zdarzenia. Dzięki temu Track A (STIX/SOAR zależne od pola
/// <c>classification</c>) nie czeka na model — pracuje od razu na realnym kształcie.
///
/// Czysta funkcja (bez zależności od Azure) → łatwa do testów jednostkowych.
/// </summary>
public static class StubClassifier
{
    public static ClassificationInfo Classify(HoneypotEvent evt) => evt.EventType switch
    {
        EventType.LoginFailed => new ClassificationInfo
        {
            KillChainPhase = KillChainPhase.Exploitation,
            Category = "brute-force",
            Sophistication = 0.2,
            Intent = "przejęcie konta (atak słownikowy)",
        },
        EventType.LoginSuccess => new ClassificationInfo
        {
            KillChainPhase = KillChainPhase.Exploitation,
            Category = "brute-force",
            Sophistication = 0.35,
            Intent = "uzyskany dostęp po ataku słownikowym",
        },
        EventType.Command => new ClassificationInfo
        {
            KillChainPhase = KillChainPhase.Installation,
            Category = "post-exploitation",
            Sophistication = 0.5,
            Intent = "działania po uzyskaniu dostępu (rozpoznanie/utrwalenie)",
        },
        EventType.HttpRequest => new ClassificationInfo
        {
            KillChainPhase = KillChainPhase.Recon,
            Category = "web-scan",
            Sophistication = 0.1,
            Intent = "skanowanie aplikacji webowych",
        },
        EventType.Connect => new ClassificationInfo
        {
            KillChainPhase = KillChainPhase.Recon,
            Category = "recon",
            Sophistication = 0.1,
            Intent = "rozpoznanie usług / skan portów",
        },
        _ => new ClassificationInfo
        {
            KillChainPhase = KillChainPhase.Recon,
            Category = "unknown",
            Sophistication = 0.1,
            Intent = "nieokreślone",
        },
    };
}
