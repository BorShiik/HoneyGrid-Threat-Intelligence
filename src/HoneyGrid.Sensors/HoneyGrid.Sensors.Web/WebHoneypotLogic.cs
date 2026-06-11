using HoneyGrid.Contracts;

namespace HoneyGrid.Sensors.Web;

/// <summary>
/// Czysta, testowalna logika honeypota webowego: ekstrakcja poświadczeń z formularzy
/// oraz budowa zdarzeń kontraktowych. Wydzielona z Program.cs, aby dało się ją pokryć testami
/// bez uruchamiania serwera HTTP.
/// </summary>
public static class WebHoneypotLogic
{
    /// <summary>
    /// Wyciąga parę poświadczeń z kolekcji pól formularza (klucz/wartość).
    /// Rozpoznaje typowe nazwy pól używane w panelach WordPress / phpMyAdmin / generycznych:
    /// username: log, user, username, email, pma_username; password: pwd, pass, password, pma_password.
    /// Zwraca null, gdy nie znaleziono ani loginu, ani hasła.
    /// </summary>
    public static CredentialPair? ExtractCredentials(IEnumerable<KeyValuePair<string, string?>> form)
    {
        string? username = null;
        string? password = null;

        foreach (var (key, value) in form)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "log" or "user" or "username" or "email" or "pma_username" or "_username":
                    username ??= value;
                    break;
                case "pwd" or "pass" or "password" or "pma_password" or "_password":
                    password ??= value;
                    break;
            }
        }

        if (username is null && password is null)
        {
            return null;
        }

        return new CredentialPair { Username = username, Password = password };
    }

    /// <summary>Buduje zdarzenie http.request dla zarejestrowanego żądania.</summary>
    public static HoneypotEvent BuildHttpRequestEvent(
        string sensorId,
        string attackerIp,
        string method,
        string? path,
        string? userAgent,
        DateTimeOffset timestamp)
        => new()
        {
            Id = Guid.NewGuid(),
            AttackerIp = attackerIp,
            SensorId = sensorId,
            SensorType = SensorType.Web,
            Timestamp = timestamp,
            EventType = EventType.HttpRequest,
            Http = new HttpInfo
            {
                Method = method,
                Path = path,
                UserAgent = userAgent,
            },
        };

    /// <summary>Buduje zdarzenie login.failed z przechwyconymi poświadczeniami.</summary>
    public static HoneypotEvent BuildLoginFailedEvent(
        string sensorId,
        string attackerIp,
        string? path,
        string? userAgent,
        CredentialPair? credentials,
        DateTimeOffset timestamp)
        => new()
        {
            Id = Guid.NewGuid(),
            AttackerIp = attackerIp,
            SensorId = sensorId,
            SensorType = SensorType.Web,
            Timestamp = timestamp,
            EventType = EventType.LoginFailed,
            Credentials = credentials,
            Http = new HttpInfo
            {
                Method = "POST",
                Path = path,
                UserAgent = userAgent,
            },
        };
}
