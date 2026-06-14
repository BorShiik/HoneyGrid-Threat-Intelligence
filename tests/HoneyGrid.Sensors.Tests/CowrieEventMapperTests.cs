using HoneyGrid.Contracts;
using HoneyGrid.Sensors.CowrieShipper;

namespace HoneyGrid.Sensors.Tests;

/// <summary>
/// Testy mappera Cowrie — karmione każdą linią z fixtures/cowrie/cowrie-sample.json
/// oraz przypadkami brzegowymi (nieznany eventid, niepoprawny JSON).
/// </summary>
public sealed class CowrieEventMapperTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "cowrie-sample.json");

    private static string[] FixtureLines() =>
        File.ReadAllLines(FixturePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

    [Fact]
    public void Fixture_istnieje_i_ma_16_linii()
    {
        var lines = FixtureLines();
        Assert.Equal(16, lines.Length);
    }

    [Fact]
    public void Connect_mapuje_sie_na_EventType_Connect()
    {
        var line = FixtureLines()[0]; // cowrie.session.connect
        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Equal(EventType.Connect, evt!.EventType);
        Assert.Equal("185.220.101.42", evt.AttackerIp);
        Assert.Equal("a1b2c3d4e5f6", evt.SessionId);
        Assert.Equal(SensorType.Ssh, evt.SensorType);
        Assert.Equal("sensor-weu-ssh-01", evt.SensorId);
    }

    [Fact]
    public void LoginFailed_wyciaga_poswiadczenia()
    {
        var line = FixtureLines()[1]; // cowrie.login.failed root/password
        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Equal(EventType.LoginFailed, evt!.EventType);
        Assert.NotNull(evt.Credentials);
        Assert.Equal("root", evt.Credentials!.Username);
        Assert.Equal("password", evt.Credentials.Password);
    }

    [Fact]
    public void LoginSuccess_wyciaga_poswiadczenia()
    {
        var line = FixtureLines()[4]; // cowrie.login.success root/qwerty123
        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Equal(EventType.LoginSuccess, evt!.EventType);
        Assert.Equal("root", evt.Credentials!.Username);
        Assert.Equal("qwerty123", evt.Credentials.Password);
    }

    [Fact]
    public void CommandInput_mapuje_pole_input()
    {
        var line = FixtureLines()[5]; // cowrie.command.input "uname -a"
        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Equal(EventType.Command, evt!.EventType);
        Assert.Equal("uname -a", evt.Command);
    }

    [Fact]
    public void FileDownload_prefiksuje_hash_sha256()
    {
        var line = FixtureLines()[8]; // cowrie.session.file_download
        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Equal(
            "sha256:8c1f4af1b1bd0e2a52c7da91b0e9c4d3f6a8b27e519c0d34e7f1a92b8c645d10",
            evt!.DownloadHash);
    }

    [Fact]
    public void LogClosed_z_ttylog_ustawia_TtyRef()
    {
        var line = FixtureLines()[10]; // cowrie.log.closed (ttylog obecny)
        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Equal(EventType.Connect, evt!.EventType);
        Assert.Equal("tty/a1b2c3d4e5f6.tty", evt.TtyRef);
    }

    [Fact]
    public void Timestamp_jest_parsowany_jako_utc()
    {
        var evt = CowrieEventMapper.Map(FixtureLines()[0]);

        Assert.NotNull(evt);
        Assert.Equal(
            DateTimeOffset.Parse("2026-06-10T03:12:41.118273Z").ToUniversalTime(),
            evt!.Timestamp.ToUniversalTime());
    }

    [Fact]
    public void Nieznany_eventid_zwraca_null_z_powodem()
    {
        const string line = """{"eventid":"cowrie.client.version","version":"SSH-2.0-libssh","src_ip":"1.2.3.4","session":"s1","sensor":"x","timestamp":"2026-06-10T03:12:41Z"}""";
        var evt = CowrieEventMapper.Map(line, out var reason);

        Assert.Null(evt);
        Assert.NotNull(reason);
        Assert.Contains("cowrie.client.version", reason);
    }

    [Fact]
    public void Niepoprawny_json_zwraca_null()
    {
        var evt = CowrieEventMapper.Map("to nie jest json", out var reason);

        Assert.Null(evt);
        Assert.NotNull(reason);
    }

    [Fact]
    public void Wszystkie_linie_fixture_mapuja_sie_na_znane_typy()
    {
        // Każda linia w próbce jest obsługiwanym eventid → nie powinno być null.
        var mapped = FixtureLines().Select(l => CowrieEventMapper.Map(l)).ToList();

        Assert.All(mapped, e => Assert.NotNull(e));
        Assert.Equal(16, mapped.Count);

        // Rozkład typów zgodny z zawartością próbki.
        Assert.Equal(2, mapped.Count(e => e!.EventType == EventType.Connect && e.SessionId is not null && e.TtyRef is null && e.Command is null));
        Assert.Equal(6, mapped.Count(e => e!.EventType == EventType.LoginFailed));
        Assert.Equal(1, mapped.Count(e => e!.EventType == EventType.LoginSuccess));
    }

    [Fact]
    public void Wszystkie_zmapowane_zdarzenia_przechodza_serializacje_kontraktu()
    {
        foreach (var line in FixtureLines())
        {
            var evt = CowrieEventMapper.Map(line);
            Assert.NotNull(evt);

            // Round-trip przez kontraktowy serializer (camelCase, enumy jako stringi).
            var json = HoneyGridJson.Serialize(evt!);
            var back = HoneyGridJson.Deserialize(json);

            Assert.NotNull(back);
            Assert.Equal(evt!.EventType, back!.EventType);
            Assert.Equal(evt.AttackerIp, back.AttackerIp);
        }
    }
}
