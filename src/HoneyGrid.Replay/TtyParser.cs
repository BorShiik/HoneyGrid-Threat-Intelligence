using System.Buffers.Binary;

namespace HoneyGrid.Replay;

/// <summary>
/// Parser binarnego formatu nagrań TTY Cowrie ("ttylog").
///
/// FORMAT REKORDU — zweryfikowany na rzeczywistym zrzucie Cowrie. Nagłówek 24 bajty
/// (6 × uint32 little-endian) + dane:
/// <code>
///   offset 0  : uint32 op        — 1=OP_OPEN, 2=OP_CLOSE, 3=OP_WRITE (rekord danych)
///   offset 4  : uint32 (0)       — pole zarezerwowane (zawsze 0)
///   offset 8  : uint32 length    — liczba bajtów danych po nagłówku (0 dla OPEN/CLOSE)
///   offset 12 : uint32 direction — 1=TYPE_INPUT (atakujący 'i'), 2=TYPE_OUTPUT (honeypot 'o')
///   offset 16 : uint32 sec       — sekundy znacznika czasu (epoch)
///   offset 20 : uint32 usec      — mikrosekundy znacznika czasu
///   offset 24 : byte[length]     — surowe dane terminala (UTF-8 / sekwencje ANSI / binaria)
/// </code>
///
/// Rekordy danych mają op=OP_WRITE(3); kierunek 'i'/'o' niesie pole <c>direction</c>
/// (NIE op). Rekordy OP_OPEN/OP_CLOSE wyznaczają znacznik czasu (pierwszy = punkt
/// zerowy), ale nie produkują klatek (brak danych).
///
/// OffsetMs klatki = (czas rekordu − czas pierwszego rekordu) w ms, gdzie
/// czas = sec * 1000 + usec / 1000. Parser jest defensywny: obcięte/uszkodzone
/// dane → łagodne zatrzymanie (zwraca klatki sparsowane do tej pory, bez wyjątku).
/// </summary>
public static class TtyParser
{
    /// <summary>Rozmiar nagłówka pojedynczego rekordu w bajtach.</summary>
    public const int HeaderSize = 24;

    private const uint OpWrite = 3;   // rekord danych terminala (jedyny niosący payload)
    private const uint TypeInput = 1; // direction: dane od atakującego → 'i' (TYPE_OUTPUT=2 → 'o')

    /// <summary>
    /// Parsuje surowy strumień bajtów nagrania TTY na listę klatek.
    /// Nisko-alokacyjnie: nagłówki czytane przez <see cref="BinaryPrimitives"/> na span,
    /// dane dekodowane przez <see cref="SafeUtf8"/>. Pusty wejściowy span → pusta lista.
    /// </summary>
    public static IReadOnlyList<ReplayFrame> Parse(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        var frames = new List<ReplayFrame>();
        long firstMs = 0;
        var haveFirst = false;
        var pos = 0;

        while (pos + HeaderSize <= data.Length)
        {
            var header = data.Slice(pos, HeaderSize);
            var op = BinaryPrimitives.ReadUInt32LittleEndian(header);
            // offset 4 zarezerwowany (0); długość na offsecie 8, kierunek na 12.
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            var direction = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            var sec = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
            var usec = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);

            var dataStart = pos + HeaderSize;

            // Defensywnie: długość danych wykraczająca poza bufor → obcięty rekord, kończymy.
            if (length > (uint)(data.Length - dataStart))
            {
                break;
            }

            // Czas rekordu w ms; pierwszy rekord (zwykle OP_OPEN) wyznacza punkt zerowy.
            var recordMs = (long)sec * 1000 + usec / 1000;
            if (!haveFirst)
            {
                firstMs = recordMs;
                haveFirst = true;
            }

            // Klatki tylko dla rekordów danych (OP_WRITE z payloadem); OPEN/CLOSE/inne pomijamy.
            if (op == OpWrite && length > 0)
            {
                var offsetMs = recordMs - firstMs;
                if (offsetMs < 0)
                {
                    offsetMs = 0; // ochrona przed nie-monotonicznymi znacznikami czasu
                }

                var payload = data.Slice(dataStart, (int)length);
                var type = direction == TypeInput ? 'i' : 'o';
                frames.Add(new ReplayFrame(offsetMs, type, SafeUtf8.Decode(payload)));
            }

            pos = dataStart + (int)length;
        }

        return frames;
    }
}
