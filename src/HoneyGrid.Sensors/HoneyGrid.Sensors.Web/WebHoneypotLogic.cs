using System.Net;
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

    /// <summary>
    /// Usuwa prefiks ::ffff: z adresów IPv4 zmapowanych na IPv6 (np. "::ffff:1.2.3.4" → "1.2.3.4").
    /// Inne adresy zwraca bez zmian.
    /// </summary>
    public static string NormalizeIp(string ip)
        => IPAddress.TryParse(ip, out var parsed) && parsed.IsIPv4MappedToIPv6
            ? parsed.MapToIPv4().ToString()
            : ip;

    /// <summary>Parsuje listę podsieci CIDR (np. "100.100.0.0/16") na IPNetwork; wpisy niepoprawne pomija.</summary>
    public static IReadOnlyList<IPNetwork> ParseTrustedNetworks(IEnumerable<string> cidrs)
    {
        var networks = new List<IPNetwork>();
        foreach (var cidr in cidrs)
        {
            if (IPNetwork.TryParse(cidr.Trim(), out var network))
            {
                networks.Add(network);
            }
        }

        return networks;
    }

    /// <summary>
    /// Czy połączenie przyszło z zaufanego proxy (ingress/envoy Container Apps)?
    /// Loopback jest zawsze zaufany (lokalny development); adresy IPv4-mapped są
    /// najpierw sprowadzane do IPv4, żeby pasowały do podsieci IPv4.
    /// </summary>
    public static bool IsTrustedProxy(IPAddress? remote, IReadOnlyList<IPNetwork> trustedNetworks)
    {
        if (remote is null)
        {
            return false;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        if (IPAddress.IsLoopback(remote))
        {
            return true;
        }

        foreach (var network in trustedNetworks)
        {
            if (network.Contains(remote))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rozwiązuje prawdziwy adres atakującego na podstawie nagłówka X-Forwarded-For
    /// i adresu połączenia. Nagłówek jest honorowany TYLKO gdy bezpośrednie połączenie
    /// pochodzi z zaufanego proxy — inaczej atakujący mógłby podstawić dowolny adres.
    ///
    /// Envoy (ingress Container Apps) DOPISUJE adres klienta na koniec XFF, więc elementy
    /// wcześniejsze mogły zostać sfałszowane przez atakującego — bierzemy ostatni element
    /// (jedyny dodany przez zaufany hop). Gdy nagłówek jest pusty lub niepoprawny,
    /// wraca adres połączenia; brak obu daje "unknown".
    /// </summary>
    public static string ResolveAttackerIp(string? forwardedFor, string? remoteIp, bool remoteIsTrustedProxy)
    {
        var fallback = string.IsNullOrWhiteSpace(remoteIp) ? "unknown" : NormalizeIp(remoteIp.Trim());

        if (!remoteIsTrustedProxy || string.IsNullOrWhiteSpace(forwardedFor))
        {
            return fallback;
        }

        var hops = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hops.Length == 0)
        {
            return fallback;
        }

        var candidate = hops[^1];
        return IPAddress.TryParse(candidate, out var parsed)
            ? NormalizeIp(parsed.ToString())
            : fallback;
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
