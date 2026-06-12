using System.Text.Json;
using HoneyGrid.Contracts;
using HoneyGrid.Ingestion.Sinks;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>
/// Sanity-check kształtu dokumentu Cosmos: serializacja HoneyGridJson musi dawać
/// małe "id" (wymóg Cosmos) i "attackerIp" (ścieżka klucza partycji /attackerIp).
/// CosmosEventWriter używa dokładnie tych opcji (UseSystemTextJsonSerializerWithOptions).
/// </summary>
public sealed class CosmosDocumentShapeTests
{
    private static HoneypotEvent FullEvent() => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        AttackerIp = "203.0.113.7",
        SensorId = "ssh-eu-01",
        SensorType = SensorType.Ssh,
        Timestamp = new DateTimeOffset(2026, 6, 11, 12, 30, 0, TimeSpan.Zero),
        EventType = EventType.Command,
        SessionId = "c0ffee01",
        Geo = new GeoInfo { Country = "PL", CountryName = "Poland", City = "Kraków", Lat = 50.06, Lon = 19.94, Asn = "AS5617", Org = "Orange Polska" },
        Credentials = new CredentialPair { Username = "root", Password = "toor" },
        Command = "uname -a",
        ThreatIntel = new ThreatIntelInfo { KnownMalicious = true, Score = 92, Sources = ["AbuseIPDB"] },
        RawRef = "raw/2026/06/11/ssh-eu-01/11111111-2222-3333-4444-555555555555.json",
    };

    [Fact]
    public void SerializedDocument_HasLowercaseId_AndAttackerIp()
    {
        var json = HoneyGridJson.Serialize(FullEvent());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Cosmos wymaga właściwości "id" (małe litery) — inaczej BadRequest.
        Assert.True(root.TryGetProperty("id", out var id));
        Assert.Equal("11111111-2222-3333-4444-555555555555", id.GetString());
        Assert.False(root.TryGetProperty("Id", out _)); // żadnego PascalCase

        // Ścieżka klucza partycji kontenera to /attackerIp.
        Assert.True(root.TryGetProperty("attackerIp", out var ip));
        Assert.Equal("203.0.113.7", ip.GetString());
    }

    [Fact]
    public void SerializedDocument_UsesCamelCase_AndStringEnums()
    {
        var json = HoneyGridJson.Serialize(FullEvent());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("command", root.GetProperty("eventType").GetString()); // enum jako string
        Assert.Equal("ssh", root.GetProperty("sensorType").GetString());
        Assert.Equal("PL", root.GetProperty("geo").GetProperty("country").GetString());
        Assert.Equal(92, root.GetProperty("threatIntel").GetProperty("score").GetInt32());
    }

    [Fact]
    public void NullFields_AreOmitted_FromDocument()
    {
        var minimal = TestHelpers.SampleEvent() with { Credentials = null };
        var json = HoneyGridJson.Serialize(minimal);
        using var doc = JsonDocument.Parse(json);

        // Pola null pomijane — dokument w Cosmos nie puchnie od nulli.
        Assert.False(doc.RootElement.TryGetProperty("geo", out _));
        Assert.False(doc.RootElement.TryGetProperty("classification", out _));
        Assert.False(doc.RootElement.TryGetProperty("credentials", out _));
    }

    [Fact]
    public void RawBlobName_IsDatePartitioned_InsideRawContainer()
    {
        var evt = FullEvent();

        // Kontener nazywa się już "raw" — nazwa bloba bez powtórzonego prefiksu.
        Assert.Equal(
            "2026/06/11/ssh-eu-01/11111111-2222-3333-4444-555555555555.json",
            RawBlobWriter.GetBlobName(evt));
    }
}
