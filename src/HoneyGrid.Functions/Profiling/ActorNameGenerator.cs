namespace HoneyGrid.Functions.Profiling;

/// <summary>
/// Generuje czytelne, stabilne nazwy aktorów (np. „Miedziany Skorpion") na
/// podstawie deterministycznego ziarna (zwykle posortowane IP klastra). Ta sama
/// kombinacja źródeł zawsze daje tę samą nazwę → spójność między przebiegami
/// korelacji. Czysta funkcja.
/// </summary>
public static class ActorNameGenerator
{
    private static readonly string[] Adjectives =
    [
        "Miedziany", "Stalowy", "Cichy", "Szkarłatny", "Lazurowy", "Żelazny",
        "Widmowy", "Kobaltowy", "Burzowy", "Nocny", "Szmaragdowy", "Złoty",
        "Mroczny", "Polarny", "Rdzawy", "Srebrny",
    ];

    private static readonly string[] Nouns =
    [
        "Skorpion", "Szerszeń", "Kruk", "Szakal", "Wąż", "Wilk", "Pająk",
        "Sęp", "Rekin", "Lampart", "Bazyliszek", "Kojot", "Borsuk", "Jastrząb",
        "Mantis", "Rój",
    ];

    /// <summary>Zwraca deterministyczną nazwę dla podanego ziarna.</summary>
    public static string Generate(string seed)
    {
        var hash = Fnv1a(seed);
        var adjective = Adjectives[(int)(hash % (uint)Adjectives.Length)];
        var noun = Nouns[(int)((hash / (uint)Adjectives.Length) % (uint)Nouns.Length)];
        return $"{adjective} {noun}";
    }

    /// <summary>Stabilny identyfikator aktora dla ziarna (PK kontenera actors).</summary>
    public static string GenerateId(string seed) => $"actor-{Fnv1a(seed):x8}";

    private static uint Fnv1a(string text)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }
}
