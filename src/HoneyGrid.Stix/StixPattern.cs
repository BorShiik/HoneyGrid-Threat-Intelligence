namespace HoneyGrid.Stix;

/// <summary>
/// Builder języka wzorców STIX 2.1 (STIX Patterning Language).
/// Generuje wyrażenia comparison/observation oraz operatory temporalne
/// (FOLLOWEDBY, WITHIN, REPEATS). To serce silnika — grading sprawdza
/// dokładny kształt zwracanych łańcuchów.
/// </summary>
public static class StixPattern
{
    /// <summary>
    /// Escapuje znaki specjalne w literale STIX zgodnie ze specyfikacją:
    /// pojedynczy apostrof i backslash poprzedzane są backslashem.
    /// </summary>
    public static string EscapeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Najpierw backslash, potem apostrof — kolejność istotna, by nie podwajać escapów.
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    /// <summary>Wzorzec dla adresu IPv4: <c>[ipv4-addr:value = '...']</c>.</summary>
    public static string Ipv4(string value) =>
        $"[ipv4-addr:value = '{EscapeValue(value)}']";

    /// <summary>Wzorzec dla hasha SHA-256 pliku: <c>[file:hashes.'SHA-256' = '...']</c>.</summary>
    public static string FileSha256(string hash) =>
        $"[file:hashes.'SHA-256' = '{EscapeValue(hash)}']";

    /// <summary>Wzorzec dla konta użytkownika: <c>[user-account:account_login = '...']</c>.</summary>
    public static string UserAccount(string login) =>
        $"[user-account:account_login = '{EscapeValue(login)}']";

    /// <summary>
    /// Operator temporalny FOLLOWEDBY z ograniczeniem WITHIN:
    /// <c>[...] FOLLOWEDBY [...] WITHIN n UNIT</c> (np. "1 MINUTES").
    /// Argumenty <paramref name="a"/>/<paramref name="b"/> to gotowe wzorce obserwacji.
    /// </summary>
    public static string FollowedByWithin(string a, string b, int n, string unit) =>
        $"{a} FOLLOWEDBY {b} WITHIN {n} {unit}";

    /// <summary>
    /// Operator REPEATS: <c>(...) REPEATS n TIMES</c>.
    /// Owija wzorzec w nawias i dodaje kwalifikator powtórzeń.
    /// </summary>
    public static string Repeats(string pattern, int times) =>
        $"({pattern}) REPEATS {times} TIMES";
}
