using System.Text;
using System.Text.Json;

namespace HoneyGrid.Replay.Tests;

/// <summary>Testy parsera binarnego formatu TTY (Cowrie ttylog).</summary>
public sealed class TtyParserTests
{
    [Fact]
    public void Parse_LoginSample_ReturnsExpectedFrameCount()
    {
        var bytes = TtyFixtureBuilder.BuildLoginSample();

        var frames = TtyParser.Parse(bytes);

        Assert.Equal(5, frames.Count);
    }

    [Fact]
    public void Parse_LoginSample_DecodesDataCorrectly()
    {
        var bytes = TtyFixtureBuilder.BuildLoginSample();

        var frames = TtyParser.Parse(bytes);

        Assert.Equal("login: ", frames[0].Data);
        Assert.Equal("root", frames[1].Data);
        Assert.Equal("Password: ", frames[2].Data);
        Assert.Equal("123456", frames[3].Data);
        Assert.Equal("# ", frames[4].Data);
    }

    [Fact]
    public void Parse_LoginSample_AssignsCorrectTypes()
    {
        var bytes = TtyFixtureBuilder.BuildLoginSample();

        var frames = TtyParser.Parse(bytes);

        // OP_WRITE → 'o', OP_READ → 'i'
        Assert.Equal('o', frames[0].Type);
        Assert.Equal('i', frames[1].Type);
        Assert.Equal('o', frames[2].Type);
        Assert.Equal('i', frames[3].Type);
        Assert.Equal('o', frames[4].Type);
    }

    [Fact]
    public void Parse_FirstFrame_HasZeroOffset()
    {
        var bytes = TtyFixtureBuilder.BuildLoginSample();

        var frames = TtyParser.Parse(bytes);

        Assert.Equal(0, frames[0].OffsetMs);
    }

    [Fact]
    public void Parse_Offsets_AreMonotonicNonDecreasing()
    {
        var bytes = TtyFixtureBuilder.BuildLoginSample();

        var frames = TtyParser.Parse(bytes);

        for (var i = 1; i < frames.Count; i++)
        {
            Assert.True(frames[i].OffsetMs >= frames[i - 1].OffsetMs,
                $"klatka {i} ma offset {frames[i].OffsetMs} < {frames[i - 1].OffsetMs}");
        }
    }

    [Fact]
    public void Parse_Offsets_ComputedFromTimestamps()
    {
        var bytes = TtyFixtureBuilder.BuildLoginSample();

        var frames = TtyParser.Parse(bytes);

        // sec=1000 usec=0 → punkt zerowy. Kolejne względem niego:
        Assert.Equal(0, frames[0].OffsetMs);     // 1000.000
        Assert.Equal(500, frames[1].OffsetMs);   // 1000.500
        Assert.Equal(1000, frames[2].OffsetMs);  // 1001.000
        Assert.Equal(2000, frames[3].OffsetMs);  // 1002.000
        Assert.Equal(2250, frames[4].OffsetMs);  // 1002.250
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        var frames = TtyParser.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Empty(frames);
    }

    [Fact]
    public void Parse_TruncatedTrailingHeader_StopsGracefully()
    {
        // Jeden poprawny rekord + 5 bajtów "ogona" (za mało na nagłówek 16 B).
        var bytes = new TtyFixtureBuilder()
            .Add(TtyFixtureBuilder.OpWrite, 5, 0, "ok")
            .AddRaw([0x01, 0x02, 0x03, 0x04, 0x05])
            .ToArray();

        var frames = TtyParser.Parse(bytes);

        Assert.Single(frames);
        Assert.Equal("ok", frames[0].Data);
    }

    [Fact]
    public void Parse_TruncatedPayload_StopsGracefully()
    {
        // Poprawny rekord, potem nagłówek deklarujący 100 B danych, ale danych brak.
        var builder = new TtyFixtureBuilder()
            .Add(TtyFixtureBuilder.OpWrite, 5, 0, "first");
        // Ręcznie dopisany nagłówek z length=100 bez payloadu:
        Span<byte> badHeader = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(badHeader, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(badHeader[4..], 100);
        builder.AddRaw(badHeader.ToArray());

        var frames = TtyParser.Parse(builder.ToArray());

        Assert.Single(frames);
        Assert.Equal("first", frames[0].Data);
    }

    [Fact]
    public void Parse_NonUtf8Bytes_DoesNotThrowAndReplaces()
    {
        // Niepoprawna sekwencja UTF-8 (samotne bajty kontynuacji 0x80 0xFF).
        var bytes = new TtyFixtureBuilder()
            .Add(TtyFixtureBuilder.OpWrite, 1, 0, [0x80, 0xFF, 0xFE])
            .ToArray();

        var frames = TtyParser.Parse(bytes);

        Assert.Single(frames);
        // Bajty zostały zastąpione znakiem U+FFFD (REPLACEMENT CHARACTER), bez wyjątku.
        Assert.Contains('\uFFFD', frames[0].Data);
    }

    [Fact]
    public void Parse_UnknownOp_DefaultsToOutput()
    {
        // op nieznane (np. 7) traktujemy jako output 'o'.
        var bytes = new TtyFixtureBuilder()
            .Add(7, 1, 0, "x")
            .ToArray();

        var frames = TtyParser.Parse(bytes);

        Assert.Equal('o', frames[0].Type);
    }

    [Fact]
    public void Parse_NegativeRelativeTime_ClampedToZero()
    {
        // Drugi rekord ma WCZEŚNIEJSZY znacznik niż pierwszy → offset nie może być ujemny.
        var bytes = new TtyFixtureBuilder()
            .Add(TtyFixtureBuilder.OpWrite, 100, 0, "a")
            .Add(TtyFixtureBuilder.OpWrite, 50, 0, "b")
            .ToArray();

        var frames = TtyParser.Parse(bytes);

        Assert.Equal(0, frames[0].OffsetMs);
        Assert.Equal(0, frames[1].OffsetMs);
    }

    [Fact]
    public void ReplaySession_SerializesToCamelCaseContract()
    {
        var frames = TtyParser.Parse(TtyFixtureBuilder.BuildLoginSample());
        var session = new ReplaySession(
            SessionId: "sess-1",
            AttackerIp: "203.0.113.7",
            SensorId: "ssh-eu-01",
            StartedAt: DateTimeOffset.Parse("2026-06-13T10:00:00Z"),
            DurationMs: frames[^1].OffsetMs,
            Frames: frames);

        var json = JsonSerializer.Serialize(session);

        Assert.Contains("\"sessionId\":\"sess-1\"", json);
        Assert.Contains("\"attackerIp\":\"203.0.113.7\"", json);
        Assert.Contains("\"sensorId\":\"ssh-eu-01\"", json);
        Assert.Contains("\"durationMs\":2250", json);
        Assert.Contains("\"offsetMs\":0", json);
        Assert.Contains("\"type\":\"o\"", json);
        Assert.Contains("\"data\":\"login: \"", json);
    }

    [Fact]
    public void SafeUtf8_Decode_EmptyReturnsEmptyString()
    {
        Assert.Equal(string.Empty, SafeUtf8.Decode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void SafeUtf8_Decode_ValidUtf8RoundTrips()
    {
        var bytes = Encoding.UTF8.GetBytes("zażółć gęślą jaźń");

        Assert.Equal("zażółć gęślą jaźń", SafeUtf8.Decode(bytes));
    }
}
