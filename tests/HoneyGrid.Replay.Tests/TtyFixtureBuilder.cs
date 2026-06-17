using System.Buffers.Binary;
using System.Text;

namespace HoneyGrid.Replay.Tests;

/// <summary>
/// Buduje binarne nagrania TTY w rzeczywistym formacie Cowrie "ttylog"
/// (nagłówek 24 B: op[0], 0[4], length[8], direction[12], sec[16], usec[20] + dane)
/// w sposób reprodukowalny — używany w testach do tworzenia znanych fikstur.
/// </summary>
internal sealed class TtyFixtureBuilder
{
    // Kody operacji (pole op, offset 0).
    public const uint OpOpen = 1;
    public const uint OpClose = 2;
    public const uint OpWrite = 3; // rekord danych

    // Kierunek (pole direction, offset 12).
    public const uint Output = 2; // TYPE_OUTPUT → 'o'
    public const uint Input = 1;  // TYPE_INPUT → 'i'

    private readonly MemoryStream _stream = new();

    /// <summary>Dopisuje rekord danych: kierunek, znacznik czasu (sek/usek), dane jako UTF-8.</summary>
    public TtyFixtureBuilder Add(uint direction, uint sec, uint usec, string text)
        => Add(direction, sec, usec, Encoding.UTF8.GetBytes(text));

    /// <summary>Dopisuje rekord danych z surowymi bajtami (np. nie-UTF8). op = OP_WRITE.</summary>
    public TtyFixtureBuilder Add(uint direction, uint sec, uint usec, byte[] payload)
    {
        WriteHeader(OpWrite, (uint)payload.Length, direction, sec, usec);
        _stream.Write(payload);
        return this;
    }

    /// <summary>Dopisuje rekord sterujący (OPEN/CLOSE lub inny) bez danych.</summary>
    public TtyFixtureBuilder AddControl(uint op, uint sec, uint usec)
    {
        WriteHeader(op, 0, 0, sec, usec);
        return this;
    }

    /// <summary>Dopisuje surowe bajty (do symulacji obciętych/uszkodzonych nagłówków).</summary>
    public TtyFixtureBuilder AddRaw(byte[] raw)
    {
        _stream.Write(raw);
        return this;
    }

    private void WriteHeader(uint op, uint length, uint direction, uint sec, uint usec)
    {
        Span<byte> h = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(h, op);
        BinaryPrimitives.WriteUInt32LittleEndian(h[4..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], length);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], direction);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], sec);
        BinaryPrimitives.WriteUInt32LittleEndian(h[20..], usec);
        _stream.Write(h);
    }

    public byte[] ToArray() => _stream.ToArray();

    /// <summary>
    /// Buduje kanoniczną próbkę sesji logowania (login: / root / Password: / 123456 / # )
    /// z rosnącymi znacznikami czasu. Używana w teście i zapisywana do fixtures/tty/sample.tty.
    /// </summary>
    public static byte[] BuildLoginSample() =>
        new TtyFixtureBuilder()
            .Add(Output, 1000, 0, "login: ")
            .Add(Input, 1000, 500_000, "root")
            .Add(Output, 1001, 0, "Password: ")
            .Add(Input, 1002, 0, "123456")
            .Add(Output, 1002, 250_000, "# ")
            .ToArray();
}
