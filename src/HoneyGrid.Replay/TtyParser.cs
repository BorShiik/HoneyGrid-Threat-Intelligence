using System.Buffers.Binary;

namespace HoneyGrid.Replay;

/// <summary>
/// Parser binarnego formatu nagrań TTY Cowrie ("ttylog").
///
/// FORMAT REKORDU (little-endian), nagłówek 16 bajtów + dane:
/// <code>
///   offset 0  : uint32 op      — typ operacji (1 = OP_WRITE/output 'o', 2 = OP_READ/input 'i')
///   offset 4  : uint32 length  — liczba bajtów danych następujących po nagłówku
///   offset 8  : uint32 sec     — sekundy znacznika czasu (epoch)
///   offset 12 : uint32 usec    — mikrosekundy znacznika czasu
///   offset 16 : byte[length]   — surowe dane terminala (UTF-8 / sekwencje ANSI / binaria)
/// </code>
///
/// OffsetMs każdej klatki = (czas rekordu − czas pierwszego rekordu) w milisekundach,
/// gdzie czas = sec * 1000 + usec / 1000.
///
/// UWAGA (uczciwie): rzeczywisty format ttylog Cowrie bywa minimalnie różny między
/// wersjami. Parser celuje w udokumentowany wyżej nagłówek 16-bajtowy; testy używają
/// spreparowanych fikstur zgodnych z tym układem, a pierwszy prawdziwy zrzut Cowrie
/// posłuży do weryfikacji. Parser jest defensywny: obcięte/uszkodzone dane → łagodne
/// zatrzymanie (zwraca klatki sparsowane do tej pory, bez wyjątku).
/// </summary>
public static class TtyParser
{
    /// <summary>Rozmiar nagłówka pojedynczego rekordu w bajtach.</summary>
    public const int HeaderSize = 16;

    private const uint OpWrite = 1; // output honeypota → 'o'
    private const uint OpRead = 2;  // input atakującego → 'i'

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
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            var sec = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            var usec = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);

            var dataStart = pos + HeaderSize;

            // Defensywnie: długość danych wykraczająca poza bufor → obcięty rekord, kończymy.
            if (length > (uint)(data.Length - dataStart))
            {
                break;
            }

            var payload = data.Slice(dataStart, (int)length);

            // Czas rekordu w ms; pierwszy rekord wyznacza punkt zerowy.
            var recordMs = (long)sec * 1000 + usec / 1000;
            if (!haveFirst)
            {
                firstMs = recordMs;
                haveFirst = true;
            }

            var offsetMs = recordMs - firstMs;
            if (offsetMs < 0)
            {
                offsetMs = 0; // ochrona przed nie-monotonicznymi znacznikami czasu
            }

            var type = op == OpRead ? 'i' : 'o';
            frames.Add(new ReplayFrame(offsetMs, type, SafeUtf8.Decode(payload)));

            pos = dataStart + (int)length;
        }

        return frames;
    }
}
