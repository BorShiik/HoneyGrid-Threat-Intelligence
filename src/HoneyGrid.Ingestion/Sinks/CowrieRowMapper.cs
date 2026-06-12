using System.Text.Json;
using HoneyGrid.Contracts;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Płaski wiersz niestandardowej tabeli Cowrie_CL w Log Analytics / Sentinel.
///
/// UWAGA — PascalCase ZAMIAST camelCase: Logs Ingestion API dopasowuje klucze JSON
/// do kolumn streamDeclaration w DCR po DOKŁADNEJ nazwie (z uwzględnieniem wielkości
/// liter). Deklaracja strumienia "Custom-CowrieStream" definiuje kolumny w PascalCase
/// (TimeGenerated, AttackerIp, ...), więc ten DTO MUSI serializować się 1:1 do tych
/// nazw — to kontrakt strumienia DCR, niezależny od kontraktu HoneyGridJson (camelCase)
/// używanego w Event Hub / Cosmos / Service Bus. Dlatego serializacja wiersza odbywa
/// się WYŁĄCZNIE przez <see cref="CowrieRowMapper.SerializerOptions"/>.
/// </summary>
public sealed record CowrieRow
{
    public required DateTimeOffset TimeGenerated { get; init; }

    public required string AttackerIp { get; init; }

    public required string SensorId { get; init; }

    /// <summary>Wartość "drutowa" enuma SensorType, np. "ssh" / "web" / "rdp".</summary>
    public string? SensorType { get; init; }

    /// <summary>Wartość "drutowa" enuma EventType, np. "login.failed".</summary>
    public required string EventType { get; init; }

    public string? SessionId { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Command { get; init; }

    public string? HttpMethod { get; init; }

    public string? HttpPath { get; init; }

    public string? UserAgent { get; init; }

    public string? CountryCode { get; init; }

    public string? City { get; init; }

    public string? Asn { get; init; }

    public string? AsnOrg { get; init; }

    public int? ThreatScore { get; init; }

    public bool? KnownMalicious { get; init; }

    /// <summary>Kategoria ataku — ZAŚLEPKA Track B (klasyfikator AI), zwykle null.</summary>
    public string? Category { get; init; }

    /// <summary>Wartość "drutowa" enuma KillChainPhase — ZAŚLEPKA Track B, zwykle null.</summary>
    public string? KillChainPhase { get; init; }
}

/// <summary>
/// Czyste (testowalne, bez I/O) spłaszczanie HoneypotEvent → CowrieRow dla
/// Logs Ingestion API. Wartości enumów mapujemy na te same stringi "drutowe",
/// którymi HoneyGridJson serializuje je wszędzie indziej (parytet pilnowany testem
/// jednostkowym dla każdej wartości enuma — mapowanie nie może się rozjechać).
/// </summary>
public static class CowrieRowMapper
{
    /// <summary>
    /// Opcje serializacji wierszy Cowrie_CL: PropertyNamingPolicy = null zachowuje
    /// PascalCase nazw właściwości C# 1:1 w kluczach JSON — dokładnie tak, jak
    /// wymaga tego deklaracja strumienia DCR (zob. komentarz przy <see cref="CowrieRow"/>).
    /// NIE używać HoneyGridJson.Options (camelCase) na tej ścieżce!
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    /// <summary>Spłaszcza wzbogacone zdarzenie do wiersza tabeli Cowrie_CL.</summary>
    public static CowrieRow ToRow(HoneypotEvent evt) => new()
    {
        TimeGenerated = evt.Timestamp,
        AttackerIp = evt.AttackerIp,
        SensorId = evt.SensorId,
        SensorType = evt.SensorType is { } sensorType ? ToWire(sensorType) : null,
        EventType = ToWire(evt.EventType),
        SessionId = evt.SessionId,
        Username = evt.Credentials?.Username,
        Password = evt.Credentials?.Password,
        Command = evt.Command,
        HttpMethod = evt.Http?.Method,
        HttpPath = evt.Http?.Path,
        UserAgent = evt.Http?.UserAgent,
        CountryCode = evt.Geo?.Country,
        City = evt.Geo?.City,
        Asn = evt.Geo?.Asn,
        AsnOrg = evt.Geo?.Org,
        ThreatScore = evt.ThreatIntel?.Score,
        KnownMalicious = evt.ThreatIntel?.KnownMalicious,
        Category = evt.Classification?.Category,
        KillChainPhase = evt.Classification?.KillChainPhase is { } phase ? ToWire(phase) : null,
    };

    /// <summary>Wartość "drutowa" SensorType — parytet z HoneyGridJson pilnowany testem.</summary>
    public static string ToWire(SensorType sensorType) => sensorType switch
    {
        Contracts.SensorType.Ssh => "ssh",
        Contracts.SensorType.Web => "web",
        Contracts.SensorType.Rdp => "rdp",
        _ => throw new ArgumentOutOfRangeException(nameof(sensorType), sensorType, "Nieznany SensorType."),
    };

    /// <summary>Wartość "drutowa" EventType — parytet z HoneyGridJson pilnowany testem.</summary>
    public static string ToWire(EventType eventType) => eventType switch
    {
        Contracts.EventType.LoginFailed => "login.failed",
        Contracts.EventType.LoginSuccess => "login.success",
        Contracts.EventType.Command => "command",
        Contracts.EventType.HttpRequest => "http.request",
        Contracts.EventType.Connect => "connect",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Nieznany EventType."),
    };

    /// <summary>Wartość "drutowa" KillChainPhase — parytet z HoneyGridJson pilnowany testem.</summary>
    public static string ToWire(KillChainPhase phase) => phase switch
    {
        Contracts.KillChainPhase.Recon => "recon",
        Contracts.KillChainPhase.Weaponization => "weaponization",
        Contracts.KillChainPhase.Delivery => "delivery",
        Contracts.KillChainPhase.Exploitation => "exploitation",
        Contracts.KillChainPhase.Installation => "installation",
        Contracts.KillChainPhase.C2 => "c2",
        Contracts.KillChainPhase.Actions => "actions",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Nieznana KillChainPhase."),
    };
}
