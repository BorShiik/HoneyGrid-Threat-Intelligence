using HoneyGrid.Contracts;

namespace HoneyGrid.Functions.Tests;

/// <summary>Fabryka zdarzeń testowych — skraca budowę <see cref="HoneypotEvent"/>.</summary>
internal static class TestEvents
{
    public static HoneypotEvent Event(
        string ip,
        EventType type = EventType.LoginFailed,
        string? command = null,
        string? username = null,
        string? password = null,
        string? asn = null,
        string? country = null,
        string? countryName = null,
        double? lat = null,
        double? lon = null,
        SensorType sensorType = SensorType.Ssh,
        double? sophistication = null,
        bool malicious = false,
        string? sessionId = null,
        DateTimeOffset? timestamp = null)
    {
        var hasGeo = asn is not null || country is not null || countryName is not null || lat is not null || lon is not null;
        return new HoneypotEvent
        {
            Id = Guid.NewGuid(),
            AttackerIp = ip,
            SensorId = "test-sensor",
            SensorType = sensorType,
            Timestamp = timestamp ?? new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero),
            EventType = type,
            SessionId = sessionId,
            Command = command,
            Credentials = username is null ? null : new CredentialPair { Username = username, Password = password },
            Geo = hasGeo ? new GeoInfo { Asn = asn, Country = country, CountryName = countryName, Lat = lat, Lon = lon } : null,
            ThreatIntel = malicious ? new ThreatIntelInfo { KnownMalicious = true, Score = 90 } : null,
            Classification = sophistication is null ? null : new ClassificationInfo { Sophistication = sophistication },
        };
    }
}
