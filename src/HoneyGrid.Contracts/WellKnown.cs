namespace HoneyGrid.Contracts;

/// <summary>
/// Stałe tekstowe typów sensorów — do użycia w zapytaniach KQL,
/// filtrach Stream Analytics i konfiguracji, gdzie enum nie jest dostępny.
/// </summary>
public static class SensorTypes
{
    public const string Ssh = "ssh";
    public const string Web = "web";
    public const string Rdp = "rdp";

    /// <summary>Wszystkie znane typy sensorów.</summary>
    public static readonly IReadOnlyList<string> All = [Ssh, Web, Rdp];
}

/// <summary>
/// Stałe tekstowe typów zdarzeń — do użycia w zapytaniach KQL,
/// regułach alertów i dashboardach.
/// </summary>
public static class EventTypes
{
    public const string LoginFailed = "login.failed";
    public const string LoginSuccess = "login.success";
    public const string Command = "command";
    public const string HttpRequest = "http.request";
    public const string Connect = "connect";

    /// <summary>Wszystkie znane typy zdarzeń.</summary>
    public static readonly IReadOnlyList<string> All =
        [LoginFailed, LoginSuccess, Command, HttpRequest, Connect];
}
