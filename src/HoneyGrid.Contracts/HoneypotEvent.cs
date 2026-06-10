using System.Text.Json.Serialization;

namespace HoneyGrid.Contracts;

/// <summary>
/// Główny kontrakt zdarzenia honeypota — wspólny schemat dla wszystkich sensorów,
/// pipeline'u ingestii, klasyfikacji i eksportu STIX 2.1.
/// Serializacja: camelCase, pola null pomijane (zob. <see cref="HoneyGridJson.Options"/>).
/// </summary>
public sealed record HoneypotEvent
{
    /// <summary>Unikalny identyfikator zdarzenia.</summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>Adres IP atakującego (IPv4/IPv6).</summary>
    [JsonPropertyName("attackerIp")]
    public required string AttackerIp { get; init; }

    /// <summary>Identyfikator sensora, np. "ssh-eu-01".</summary>
    [JsonPropertyName("sensorId")]
    public required string SensorId { get; init; }

    /// <summary>Typ sensora: ssh | web | rdp.</summary>
    [JsonPropertyName("sensorType")]
    public SensorType? SensorType { get; init; }

    /// <summary>Znacznik czasu zdarzenia (UTC).</summary>
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Typ zdarzenia: login.failed | login.success | command | http.request | connect.</summary>
    [JsonPropertyName("eventType")]
    public required EventType EventType { get; init; }

    /// <summary>Identyfikator sesji w sensorze (np. sesja Cowrie).</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    /// <summary>Wzbogacenie geolokalizacyjne (GeoIP/ASN).</summary>
    [JsonPropertyName("geo")]
    public GeoInfo? Geo { get; init; }

    /// <summary>Przechwycone dane logowania (tylko zdarzenia login.*).</summary>
    [JsonPropertyName("credentials")]
    public CredentialPair? Credentials { get; init; }

    /// <summary>Pełna komenda wykonana przez atakującego (zdarzenia typu command).</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Hash pobranego artefaktu, np. "sha256:...".</summary>
    [JsonPropertyName("downloadHash")]
    public string? DownloadHash { get; init; }

    /// <summary>Szczegóły żądania HTTP (tylko sensory web).</summary>
    [JsonPropertyName("http")]
    public HttpInfo? Http { get; init; }

    /// <summary>Wzbogacenie o threat intelligence (AbuseIPDB itd.).</summary>
    [JsonPropertyName("threatIntel")]
    public ThreatIntelInfo? ThreatIntel { get; init; }

    /// <summary>Wynik klasyfikacji ataku (kill chain, kategoria, aktor).</summary>
    [JsonPropertyName("classification")]
    public ClassificationInfo? Classification { get; init; }

    /// <summary>Referencja do nagrania sesji TTY w Blob Storage.</summary>
    [JsonPropertyName("ttyRef")]
    public string? TtyRef { get; init; }

    /// <summary>Referencja do surowego zdarzenia w Blob Storage.</summary>
    [JsonPropertyName("rawRef")]
    public string? RawRef { get; init; }
}

/// <summary>Dane geolokalizacyjne atakującego.</summary>
public sealed record GeoInfo
{
    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("countryName")]
    public string? CountryName { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("lat")]
    public double? Lat { get; init; }

    [JsonPropertyName("lon")]
    public double? Lon { get; init; }

    /// <summary>Numer systemu autonomicznego, np. "AS7552".</summary>
    [JsonPropertyName("asn")]
    public string? Asn { get; init; }

    [JsonPropertyName("org")]
    public string? Org { get; init; }
}

/// <summary>Para poświadczeń przechwycona podczas próby logowania.</summary>
public sealed record CredentialPair
{
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }
}

/// <summary>Szczegóły żądania HTTP zarejestrowanego przez honeypot web.</summary>
public sealed record HttpInfo
{
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; init; }
}

/// <summary>Wzbogacenie o dane threat intelligence.</summary>
public sealed record ThreatIntelInfo
{
    [JsonPropertyName("knownMalicious")]
    public bool? KnownMalicious { get; init; }

    /// <summary>Źródła TI, np. ["AbuseIPDB"].</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>Wynik reputacji 0–100.</summary>
    [JsonPropertyName("score")]
    public int? Score { get; init; }
}

/// <summary>Wynik klasyfikacji ataku.</summary>
public sealed record ClassificationInfo
{
    [JsonPropertyName("killChainPhase")]
    public KillChainPhase? KillChainPhase { get; init; }

    /// <summary>Kategoria ataku, np. "mirai_botnet".</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>Poziom zaawansowania 0.0–1.0.</summary>
    [JsonPropertyName("sophistication")]
    public double? Sophistication { get; init; }

    /// <summary>Domniemany cel atakującego, np. "cryptominer".</summary>
    [JsonPropertyName("intent")]
    public string? Intent { get; init; }

    /// <summary>Identyfikator skorelowanego aktora, np. "actor-0047".</summary>
    [JsonPropertyName("actorId")]
    public string? ActorId { get; init; }
}
