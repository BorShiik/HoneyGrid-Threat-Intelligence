using System.Text.Json;
using HoneyGrid.Contracts;

namespace HoneyGrid.Functions.Ai;

/// <summary>
/// Buduje prompty dla klasyfikatora Azure OpenAI. Czysta logika (bez sieci) →
/// łatwa do testów i do podglądu, co dokładnie trafia do modelu.
///
/// Kontrakt odpowiedzi: model MUSI zwrócić wyłącznie tablicę JSON, jeden obiekt
/// na zdarzenie, w tej samej kolejności co wejście. Parsowanie i tak jest
/// odporne na odstępstwa (zob. <see cref="ClassificationResponseParser"/>).
/// </summary>
public static class ClassificationPrompt
{
    public const string System =
        """
        Jesteś analitykiem SOC klasyfikującym zdarzenia z honeypotów.
        Dla KAŻDEGO zdarzenia wejściowego zwróć obiekt z polami:
          - killChainPhase: jedna z: recon | weaponization | delivery | exploitation | installation | c2 | actions
          - category: krótka kategoria ataku (np. brute-force, botnet, cryptomining, web-scan, webshell)
          - sophistication: liczba 0.0–1.0 (0 = prosty bot/skrypt, 1 = zaawansowany operator)
          - intent: jedno zdanie po polsku opisujące domniemany cel atakującego
        Odpowiedz WYŁĄCZNIE tablicą JSON, w tej samej kolejności co wejście,
        bez markdown, bez komentarzy, bez dodatkowego tekstu.
        """;

    /// <summary>Buduje prompt użytkownika ze zwięzłą reprezentacją wsadu zdarzeń.</summary>
    public static string BuildUser(IReadOnlyList<HoneypotEvent> batch)
    {
        var compact = new object[batch.Count];
        for (var i = 0; i < batch.Count; i++)
        {
            var e = batch[i];
            compact[i] = new
            {
                index = i,
                eventType = e.EventType.ToString(),
                sensorType = e.SensorType?.ToString(),
                command = e.Command,
                httpPath = e.Http?.Path,
                username = e.Credentials?.Username,
                country = e.Geo?.Country,
                asn = e.Geo?.Asn,
                knownMalicious = e.ThreatIntel?.KnownMalicious,
            };
        }

        var json = JsonSerializer.Serialize(compact, CompactOptions);
        return $"Sklasyfikuj {batch.Count} zdarzeń (zwróć tablicę {batch.Count} obiektów):\n{json}";
    }

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
