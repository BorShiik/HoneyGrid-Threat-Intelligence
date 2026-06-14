using System.Text.Json.Serialization;

namespace HoneyGrid.Functions.Aggregation;

// Dokumenty predrachowanych agregatów zapisywane do kontenera Cosmos
// `aggregates` (PK /bucket). Kształt JSON celowo zgodny z DTO dashboardu
// w HoneyGrid.Api (Features/Stats) oraz z openapi.yaml — dzięki temu API może
// odczytać dokument WPROST do swojego DTO (pola id/bucket są wtedy ignorowane).
//
// Bucket = id = "overview" | "geo" | "credentials" (jeden dokument na widok).

/// <summary>Agregat nagłówka dashboardu (bucket "overview").</summary>
public sealed record OverviewAggregate
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "overview";

    [JsonPropertyName("bucket")]
    public string Bucket { get; init; } = "overview";

    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; init; }

    [JsonPropertyName("eventsLast24h")]
    public long EventsLast24h { get; init; }

    [JsonPropertyName("uniqueAttackers")]
    public long UniqueAttackers { get; init; }

    [JsonPropertyName("activeSessions")]
    public long ActiveSessions { get; init; }

    [JsonPropertyName("topCountries")]
    public IReadOnlyList<CountryCount> TopCountries { get; init; } = [];

    [JsonPropertyName("eventsBySensorType")]
    public IReadOnlyDictionary<string, long> EventsBySensorType { get; init; } = new Dictionary<string, long>();

    [JsonPropertyName("eventsByType")]
    public IReadOnlyDictionary<string, long> EventsByType { get; init; } = new Dictionary<string, long>();

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CountryCount
{
    [JsonPropertyName("country")]
    public string Country { get; init; } = "";

    [JsonPropertyName("countryName")]
    public string CountryName { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

/// <summary>Agregat mapy ataków (bucket "geo").</summary>
public sealed record GeoAggregate
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "geo";

    [JsonPropertyName("bucket")]
    public string Bucket { get; init; } = "geo";

    [JsonPropertyName("points")]
    public IReadOnlyList<GeoPoint> Points { get; init; } = [];

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record GeoPoint
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

/// <summary>Agregat analizy poświadczeń (bucket "credentials").</summary>
public sealed record CredentialAggregate
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "credentials";

    [JsonPropertyName("bucket")]
    public string Bucket { get; init; } = "credentials";

    [JsonPropertyName("topUsernames")]
    public IReadOnlyList<CountedUsername> TopUsernames { get; init; } = [];

    [JsonPropertyName("topPasswords")]
    public IReadOnlyList<CountedPassword> TopPasswords { get; init; } = [];

    [JsonPropertyName("topPairs")]
    public IReadOnlyList<CredentialPairCount> TopPairs { get; init; } = [];

    [JsonPropertyName("totalAttempts")]
    public long TotalAttempts { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CountedUsername
{
    [JsonPropertyName("username")]
    public string Username { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

public sealed record CountedPassword
{
    [JsonPropertyName("password")]
    public string Password { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}

public sealed record CredentialPairCount
{
    [JsonPropertyName("username")]
    public string Username { get; init; } = "";

    [JsonPropertyName("password")]
    public string Password { get; init; } = "";

    [JsonPropertyName("count")]
    public long Count { get; init; }
}
