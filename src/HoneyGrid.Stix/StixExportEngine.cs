using HoneyGrid.Contracts;

namespace HoneyGrid.Stix;

/// <summary>
/// Silnik eksportu STIX 2.1 — szkielet.
///
/// Odpowiedzialność modułu:
///  - mapowanie <see cref="HoneypotEvent"/> na obiekty STIX 2.1:
///    Indicator (IP/hash), Malware, Attack-Pattern (MITRE ATT&amp;CK), Threat-Actor,
///  - budowa STIX Bundle do publikacji przez endpoint TAXII-lite w HoneyGrid.Api.
/// </summary>
public static class StixExportEngine
{
    /// <summary>Wersja specyfikacji STIX wspierana przez silnik.</summary>
    public const string StixVersion = "2.1";

    // TODO (Track C, Tydzień 7): modele SDO/SCO (Indicator, Malware, Bundle) jako rekordy C#.
    // TODO (Track C, Tydzień 7): MapToIndicator(HoneypotEvent) — wzorzec STIX pattern
    //                            "[ipv4-addr:value = '...']" z attackerIp.
    // TODO (Track C, Tydzień 8): BuildBundle(IEnumerable<HoneypotEvent>) + deduplikacja po IP/hash.
    public static string MapToIndicatorPattern(HoneypotEvent evt) =>
        $"[ipv4-addr:value = '{evt.AttackerIp}']";
}
