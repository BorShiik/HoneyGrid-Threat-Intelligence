using System.Text;

namespace HoneyGrid.Replay;

/// <summary>
/// Pomocnik dekodowania bajtów na tekst UTF-8 w sposób "bezpieczny" (lossy):
/// niepoprawne sekwencje bajtów są zastępowane znakiem U+FFFD (REPLACEMENT CHARACTER)
/// zamiast rzucać wyjątek. Niezbędne, bo nagrania TTY zawierają surowe bajty terminala
/// (sekwencje ANSI, dane binarne), które nie zawsze są poprawnym UTF-8.
/// </summary>
public static class SafeUtf8
{
    // Domyślny Encoding.UTF8 używa ReplacementFallback wstawiającego U+FFFD
    // (nie rzuca — UTF8Encoding skonstruowany bez throwOnInvalidBytes).
    private static readonly Encoding Decoder = Encoding.UTF8;

    /// <summary>Dekoduje bajty na string UTF-8, zastępując niepoprawne sekwencje.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes) =>
        bytes.IsEmpty ? string.Empty : Decoder.GetString(bytes);
}
