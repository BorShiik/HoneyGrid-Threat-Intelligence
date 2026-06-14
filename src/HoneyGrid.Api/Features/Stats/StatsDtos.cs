using System.Text.Json.Serialization;

namespace HoneyGrid.Api.Features.Stats;

// DTO kontraktu dashboardu (Track B). Kształt celowo zgodny z frontendowymi
// typami w HoneyGrid.Web/src/types/api.ts (camelCase via HoneyGridJson.Options).

/// <summary>Podsumowanie nagłówka dashboardu (GET /api/stats/overview).</summary>
public sealed record StatsOverviewDto
{
    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; init; }

    [JsonPropertyName("eventsLast24h")]
    public long EventsLast24h { get; init; }

    [JsonPropertyName("uniqueAttackers")]
    public long UniqueAttackers { get; init; }

    [JsonPropertyName("activeSessions")]
    public long ActiveSessions { get; init; }

    [JsonPropertyName("topCountries")]
    public IReadOnlyList<CountryCountDto> TopCountries { get; init; } = [];

    /// <summary>Liczba zdarzeń wg typu sensora (ssh|web|rdp).</summary>
    [JsonPropertyName("eventsBySensorType")]
    public IReadOnlyDictionary<string, long> EventsBySensorType { get; init; } =
        new Dictionary<string, long>();

    /// <summary>Liczba zdarzeń wg typu zdarzenia (login.failed|...|connect).</summary>
    [JsonPropertyName("eventsByType")]
    public IReadOnlyDictionary<string, long> EventsByType { get; init; } =
        new Dictionary<string, long>();
}

public sealed record CountryCountDto
{
    [JsonPropertyName("country")]
    public string Country { get; init; } = "";

    [JsonPropertyName("countryName")]
    public string CountryName { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

/// <summary>Punkty geograficzne do mapy ataków (GET /api/stats/geo).</summary>
public sealed record GeoStatsDto
{
    [JsonPropertyName("points")]
    public IReadOnlyList<GeoStatPointDto> Points { get; init; } = [];

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record GeoStatPointDto
{
    [JsonPropertyName("country")]
    public string Country { get; init; } = "";

    [JsonPropertyName("countryName")]
    public string CountryName { get; init; } = "";

    [JsonPropertyName("lat")]
    public double Lat { get; init; }

    [JsonPropertyName("lon")]
    public double Lon { get; init; }

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

/// <summary>Analiza poświadczeń (GET /api/stats/credentials).</summary>
public sealed record CredentialStatsDto
{
    [JsonPropertyName("topUsernames")]
    public IReadOnlyList<UsernameCountDto> TopUsernames { get; init; } = [];

    [JsonPropertyName("topPasswords")]
    public IReadOnlyList<PasswordCountDto> TopPasswords { get; init; } = [];

    [JsonPropertyName("topPairs")]
    public IReadOnlyList<CredentialPairCountDto> TopPairs { get; init; } = [];

    [JsonPropertyName("totalAttempts")]
    public long TotalAttempts { get; init; }
}

public sealed record UsernameCountDto
{
    [JsonPropertyName("username")]
    public string Username { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

public sealed record PasswordCountDto
{
    [JsonPropertyName("password")]
    public string Password { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

public sealed record CredentialPairCountDto
{
    [JsonPropertyName("username")]
    public string Username { get; init; } = "";

    [JsonPropertyName("password")]
    public string Password { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}
