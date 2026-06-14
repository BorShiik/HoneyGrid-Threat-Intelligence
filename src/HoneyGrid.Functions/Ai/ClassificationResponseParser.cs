using System.Text.Json;
using HoneyGrid.Contracts;

namespace HoneyGrid.Functions.Ai;

/// <summary>
/// Odporny parser odpowiedzi modelu na <see cref="ClassificationInfo"/>.
///
/// LLM bywa nieprzewidywalny, więc parser:
///  - zdejmuje opłotkowanie markdown (```json … ```),
///  - wycina fragment od pierwszego '[' do ostatniego ']',
///  - toleruje brakujące/niepełne elementy (zwraca null na danej pozycji),
///  - mapuje fazę kill chain elastycznie (synonimy, wielkość liter).
///
/// Zwraca tablicę o długości <c>expectedCount</c>; pozycje, których nie udało się
/// sparsować, są <c>null</c> — wołający podstawia wtedy klasyfikator zastępczy.
/// Czysta logika → w pełni testowalna.
/// </summary>
public static class ClassificationResponseParser
{
    public static IReadOnlyList<ClassificationInfo?> Parse(string? modelText, int expectedCount)
    {
        var result = new ClassificationInfo?[Math.Max(expectedCount, 0)];
        if (string.IsNullOrWhiteSpace(modelText) || expectedCount <= 0) return result;

        var json = ExtractJsonArray(modelText);
        if (json is null) return result;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return result;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            var i = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (i >= result.Length) break;
                result[i] = element.ValueKind == JsonValueKind.Object ? MapElement(element) : null;
                i++;
            }
        }

        return result;
    }

    private static ClassificationInfo MapElement(JsonElement element)
    {
        return new ClassificationInfo
        {
            KillChainPhase = element.TryGetProperty("killChainPhase", out var phase)
                && phase.ValueKind == JsonValueKind.String
                ? ParsePhase(phase.GetString())
                : null,
            Category = GetString(element, "category"),
            Sophistication = GetSophistication(element),
            Intent = GetString(element, "intent"),
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double? GetSophistication(JsonElement element)
    {
        if (!element.TryGetProperty("sophistication", out var v)) return null;

        double value;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) value = d;
        else if (v.ValueKind == JsonValueKind.String && double.TryParse(
                     v.GetString(), System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var ds)) value = ds;
        else return null;

        return Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>Mapuje nazwę fazy (z synonimami) na <see cref="KillChainPhase"/>.</summary>
    public static KillChainPhase? ParsePhase(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "recon" or "reconnaissance" => KillChainPhase.Recon,
            "weaponization" => KillChainPhase.Weaponization,
            "delivery" => KillChainPhase.Delivery,
            "exploitation" or "exploit" => KillChainPhase.Exploitation,
            "installation" or "install" or "persistence" => KillChainPhase.Installation,
            "c2" or "command-and-control" or "command and control" or "c&c" => KillChainPhase.C2,
            "actions" or "actions-on-objectives" or "actions on objectives" => KillChainPhase.Actions,
            _ => null,
        };
    }

    /// <summary>Wycina fragment tablicy JSON z surowego tekstu modelu.</summary>
    public static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
    }
}
