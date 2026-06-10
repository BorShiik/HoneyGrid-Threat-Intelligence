using System.Text.Json.Serialization;

namespace HoneyGrid.Contracts;

/// <summary>Typ sensora honeypot. W JSON serializowany jako "ssh" | "web" | "rdp".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SensorType>))]
public enum SensorType
{
    [JsonStringEnumMemberName("ssh")]
    Ssh,

    [JsonStringEnumMemberName("web")]
    Web,

    [JsonStringEnumMemberName("rdp")]
    Rdp,
}

/// <summary>
/// Typ zdarzenia honeypota. Wartości JSON zawierają kropki
/// ("login.failed", "http.request"), stąd jawne mapowanie nazw.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EventType>))]
public enum EventType
{
    [JsonStringEnumMemberName("login.failed")]
    LoginFailed,

    [JsonStringEnumMemberName("login.success")]
    LoginSuccess,

    [JsonStringEnumMemberName("command")]
    Command,

    [JsonStringEnumMemberName("http.request")]
    HttpRequest,

    [JsonStringEnumMemberName("connect")]
    Connect,
}

/// <summary>Faza Cyber Kill Chain przypisana zdarzeniu przez silnik klasyfikacji.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KillChainPhase>))]
public enum KillChainPhase
{
    [JsonStringEnumMemberName("recon")]
    Recon,

    [JsonStringEnumMemberName("weaponization")]
    Weaponization,

    [JsonStringEnumMemberName("delivery")]
    Delivery,

    [JsonStringEnumMemberName("exploitation")]
    Exploitation,

    [JsonStringEnumMemberName("installation")]
    Installation,

    [JsonStringEnumMemberName("c2")]
    C2,

    [JsonStringEnumMemberName("actions")]
    Actions,
}
