using HoneyGrid.Contracts;

namespace HoneyGrid.Functions.Aggregation;

/// <summary>
/// Wylicza predrachowane agregaty dashboardu ze zdarzeń. Czysta logika
/// (deterministyczna, bez Azure) → w pełni testowalna. Funkcja Timer
/// (BuildAggregates) zapisuje wynik do kontenera <c>aggregates</c>, a API
/// odczytuje gotowe dokumenty zamiast liczyć na żywo.
/// </summary>
public static class AggregateBuilder
{
    private const int TopCountriesLimit = 5;
    private const int TopCredentialsLimit = 10;

    public static OverviewAggregate BuildOverview(IReadOnlyCollection<HoneypotEvent> events, DateTimeOffset now)
    {
        var since24h = now.AddHours(-24);
        var sessionSince = now.AddMinutes(-5);

        var topCountries = events
            .Where(e => !string.IsNullOrWhiteSpace(e.Geo?.Country))
            .GroupBy(e => (e.Geo!.Country!, e.Geo!.CountryName ?? e.Geo!.Country!))
            .Select(g => new CountryCount { Country = g.Key.Item1, CountryName = g.Key.Item2, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Country, StringComparer.Ordinal)
            .Take(TopCountriesLimit)
            .ToList();

        var bySensor = events
            .Where(e => e.SensorType is not null)
            .GroupBy(e => SensorTypeKey(e.SensorType!.Value))
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var byType = events
            .GroupBy(e => EventTypeKey(e.EventType))
            .ToDictionary(g => g.Key, g => (long)g.Count());

        return new OverviewAggregate
        {
            TotalEvents = events.Count,
            EventsLast24h = events.Count(e => e.Timestamp > since24h),
            UniqueAttackers = events.Select(e => e.AttackerIp).Distinct(StringComparer.Ordinal).Count(),
            ActiveSessions = events
                .Where(e => e.Timestamp > sessionSince && !string.IsNullOrWhiteSpace(e.SessionId))
                .Select(e => e.SessionId!)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            TopCountries = topCountries,
            EventsBySensorType = bySensor,
            EventsByType = byType,
            UpdatedAt = now,
        };
    }

    public static GeoAggregate BuildGeo(IReadOnlyCollection<HoneypotEvent> events, DateTimeOffset now)
    {
        var points = events
            .Where(e => e.Geo?.Lat is not null && e.Geo?.Lon is not null && !string.IsNullOrWhiteSpace(e.Geo?.Country))
            .GroupBy(e => (e.Geo!.Country!, e.Geo!.CountryName ?? e.Geo!.Country!))
            .Select(g => new GeoPoint
            {
                Country = g.Key.Item1,
                CountryName = g.Key.Item2,
                Lat = g.Average(e => e.Geo!.Lat!.Value),
                Lon = g.Average(e => e.Geo!.Lon!.Value),
                Count = g.Count(),
            })
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Country, StringComparer.Ordinal)
            .ToList();

        return new GeoAggregate { Points = points, UpdatedAt = now };
    }

    public static CredentialAggregate BuildCredentials(IReadOnlyCollection<HoneypotEvent> events, DateTimeOffset now)
    {
        var withUser = events.Where(e => !string.IsNullOrWhiteSpace(e.Credentials?.Username)).ToList();

        var topUsernames = withUser
            .GroupBy(e => e.Credentials!.Username!)
            .Select(g => new CountedUsername { Username = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Username, StringComparer.Ordinal)
            .Take(TopCredentialsLimit)
            .ToList();

        var topPasswords = events
            .Where(e => !string.IsNullOrWhiteSpace(e.Credentials?.Password))
            .GroupBy(e => e.Credentials!.Password!)
            .Select(g => new CountedPassword { Password = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Password, StringComparer.Ordinal)
            .Take(TopCredentialsLimit)
            .ToList();

        var topPairs = withUser
            .GroupBy(e => (e.Credentials!.Username!, e.Credentials!.Password ?? ""))
            .Select(g => new CredentialPairCount { Username = g.Key.Item1, Password = g.Key.Item2, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Username, StringComparer.Ordinal)
            .Take(TopCredentialsLimit)
            .ToList();

        return new CredentialAggregate
        {
            TopUsernames = topUsernames,
            TopPasswords = topPasswords,
            TopPairs = topPairs,
            TotalAttempts = withUser.Count,
            UpdatedAt = now,
        };
    }

    /// <summary>Klucz JSON typu zdarzenia ("login.failed" itd.) — zgodny z frontendem.</summary>
    public static string EventTypeKey(EventType type) => type switch
    {
        EventType.LoginFailed => EventTypes.LoginFailed,
        EventType.LoginSuccess => EventTypes.LoginSuccess,
        EventType.Command => EventTypes.Command,
        EventType.HttpRequest => EventTypes.HttpRequest,
        EventType.Connect => EventTypes.Connect,
        _ => "unknown",
    };

    /// <summary>Klucz JSON typu sensora ("ssh" | "web" | "rdp").</summary>
    public static string SensorTypeKey(SensorType type) => type switch
    {
        SensorType.Ssh => SensorTypes.Ssh,
        SensorType.Web => SensorTypes.Web,
        SensorType.Rdp => SensorTypes.Rdp,
        _ => "unknown",
    };
}
