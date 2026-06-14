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
        SensorType sensorType = SensorType.Ssh,
        double? sophistication = null,
        bool malicious = false,
        DateTimeOffset? timestamp = null)
    {
        return new HoneypotEvent
        {
            Id = Guid.NewGuid(),
            AttackerIp = ip,
            SensorId = "test-sensor",
            SensorType = sensorType,
            Timestamp = timestamp ?? new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero),
            EventType = type,
            Command = command,
            Credentials = username is null ? null : new CredentialPair { Username = username, Password = password },
            Geo = (asn is null && country is null) ? null : new GeoInfo { Asn = asn, Country = country },
            ThreatIntel = malicious ? new ThreatIntelInfo { KnownMalicious = true, Score = 90 } : null,
            Classification = sophistication is null ? null : new ClassificationInfo { Sophistication = sophistication },
        };
    }
}
