using System.Buffers.Binary;
using System.Text;

namespace HoneyGrid.Replay.Tests;

/// <summary>
/// Buduje binarne nagrania TTY w formacie Cowrie "ttylog" (nagłówek 16 B + dane)
/// w sposób reprodukowalny — używany w testach do tworzenia znanych fikstur.
/// </summary>
internal sealed class TtyFixtureBuilder
{
    public const uint OpWrite = 1; // output 'o'
    public const uint OpRead = 2;  // input 'i'

    private readonly MemoryStream _stream = new();

    /// <summary>Dopisuje rekord: op, znacznik czasu (sek/usek), dane jako UTF-8.</summary>
    public TtyFixtureBuilder Add(uint op, uint sec, uint usec, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        return Add(op, sec, usec, payload);
    }

    /// <summary>Dopisuje rekord z surowymi bajtami danych (np. nie-UTF8).</summary>
    public TtyFixtureBuilder Add(uint op, uint sec, uint usec, byte[] payload)
    {
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, op);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], sec);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], usec);
        _stream.Write(header);
        _stream.Write(payload);
        return this;
    }

    /// <summary>Dopisuje surowe bajty (do symulacji obciętych/uszkodzonych nagłówków).</summary>
    public TtyFixtureBuilder AddRaw(byte[] raw)
    {
        _stream.Write(raw);
        return this;
    }

    public byte[] ToArray() => _stream.ToArray();

    /// <summary>
    /// Buduje kanoniczną próbkę sesji logowania (login: / root / Password: / 123456 / # )
    /// z rosnącymi znacznikami czasu. Używana w teście i zapisywana do fixtures/tty/sample.tty.
    /// </summary>
    public static byte[] BuildLoginSample() =>
        new TtyFixtureBuilder()
            .Add(OpWrite, 1000, 0, "login: ")
            .Add(OpRead, 1000, 500_000, "root")
            .Add(OpWrite, 1001, 0, "Password: ")
            .Add(OpRead, 1002, 0, "123456")
            .Add(OpWrite, 1002, 250_000, "# ")
            .ToArray();
}
