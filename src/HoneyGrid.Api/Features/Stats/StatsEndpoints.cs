using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;

namespace HoneyGrid.Api.Features.Stats;

/// <summary>
/// Endpointy statystyk dashboardu (Track B):
///  - <c>GET /api/stats/overview</c>    — KPI nagłówka,
///  - <c>GET /api/stats/geo</c>         — wiadra geograficzne do mapy ataków,
///  - <c>GET /api/stats/credentials</c> — analiza poświadczeń.
///
/// Implementacja liczy agregaty „na żywo" z kontenera <c>events</c>. Docelowo
/// (Tydzień 4) zastąpi to predrachowany kontener <c>aggregates</c> zasilany
/// funkcją Timer — endpointy będą wtedy tylko czytać gotowe dokumenty.
///
/// Zasada defensywna: błąd Cosmos → sensowne wartości puste, nigdy 500.
/// </summary>
public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats/overview", OverviewAsync).WithName("GetStatsOverview").WithTags("Stats");
        app.MapGet("/api/stats/geo", GeoAsync).WithName("GetStatsGeo").WithTags("Stats");
        app.MapGet("/api/stats/credentials", CredentialsAsync).WithName("GetStatsCredentials").WithTags("Stats");
        return app;
    }

    // ── /api/stats/overview ───────────────────────────────────────────────
    private static async Task<IResult> OverviewAsync(
        CosmosClient cosmos,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HoneyGrid.Api.Stats");
        var container = GetEventsContainer(cosmos, config);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24).ToString("O");
        var sessionCutoff = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");

        var totalEvents = await ScalarAsync(container,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c"), logger, ct);

        var eventsLast24h = await ScalarAsync(container,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.timestamp > @cutoff")
                .WithParameter("@cutoff", cutoff), logger, ct);

        var uniqueAttackers = await ScalarAsync(container,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM (SELECT DISTINCT VALUE c.attackerIp FROM c) AS a"),
            logger, ct);

        var activeSessions = await ScalarAsync(container,
            new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM (SELECT DISTINCT VALUE c.sessionId FROM c " +
                "WHERE IS_DEFINED(c.sessionId) AND c.timestamp > @cutoff) AS s")
                .WithParameter("@cutoff", sessionCutoff), logger, ct);

        var topCountries = await GroupCountAsync(container,
            "SELECT c.geo.country AS key, c.geo.countryName AS label, COUNT(1) AS count FROM c " +
            "WHERE IS_DEFINED(c.geo.country) GROUP BY c.geo.country, c.geo.countryName",
            logger, ct);

        var bySensor = await GroupCountAsync(container,
            "SELECT c.sensorType AS key, COUNT(1) AS count FROM c WHERE IS_DEFINED(c.sensorType) GROUP BY c.sensorType",
            logger, ct);

        var byType = await GroupCountAsync(container,
            "SELECT c.eventType AS key, COUNT(1) AS count FROM c WHERE IS_DEFINED(c.eventType) GROUP BY c.eventType",
            logger, ct);

        var dto = new StatsOverviewDto
        {
            TotalEvents = totalEvents,
            EventsLast24h = eventsLast24h,
            UniqueAttackers = uniqueAttackers,
            ActiveSessions = activeSessions,
            TopCountries = topCountries
                .OrderByDescending(g => g.Count)
                .Take(5)
                .Select(g => new CountryCountDto { Country = g.Key ?? "??", CountryName = g.Label ?? g.Key ?? "", Count = g.Count })
                .ToList(),
            EventsBySensorType = bySensor.ToDictionary(g => g.Key ?? "unknown", g => g.Count),
            EventsByType = byType.ToDictionary(g => g.Key ?? "unknown", g => g.Count),
        };

        return Results.Json(dto, HoneyGridJson.Options);
    }

    // ── /api/stats/geo ────────────────────────────────────────────────────
    private static async Task<IResult> GeoAsync(
        CosmosClient cosmos,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HoneyGrid.Api.Stats");
        var container = GetEventsContainer(cosmos, config);

        var rows = new List<GeoStatPointDto>();
        try
        {
            var query = new QueryDefinition(
                "SELECT c.geo.country AS country, c.geo.countryName AS countryName, " +
                "AVG(c.geo.lat) AS lat, AVG(c.geo.lon) AS lon, COUNT(1) AS count FROM c " +
                "WHERE IS_DEFINED(c.geo.lat) AND IS_DEFINED(c.geo.lon) " +
                "GROUP BY c.geo.country, c.geo.countryName");

            using var iterator = container.GetItemQueryIterator<GeoRow>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                foreach (var r in page)
                {
                    if (string.IsNullOrWhiteSpace(r.Country)) continue;
                    rows.Add(new GeoStatPointDto
                    {
                        Country = r.Country,
                        CountryName = r.CountryName ?? r.Country,
                        Lat = r.Lat,
                        Lon = r.Lon,
                        Count = r.Count,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stats/geo: zapytanie Cosmos nieudane, zwracam pustą mapę.");
        }

        var dto = new GeoStatsDto
        {
            Points = rows.OrderByDescending(p => p.Count).ToList(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return Results.Json(dto, HoneyGridJson.Options);
    }

    // ── /api/stats/credentials ────────────────────────────────────────────
    private static async Task<IResult> CredentialsAsync(
        CosmosClient cosmos,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HoneyGrid.Api.Stats");
        var container = GetEventsContainer(cosmos, config);

        var byUser = await GroupCountAsync(container,
            "SELECT c.credentials.username AS key, COUNT(1) AS count FROM c " +
            "WHERE IS_DEFINED(c.credentials.username) GROUP BY c.credentials.username",
            logger, ct);

        var byPass = await GroupCountAsync(container,
            "SELECT c.credentials.password AS key, COUNT(1) AS count FROM c " +
            "WHERE IS_DEFINED(c.credentials.password) GROUP BY c.credentials.password",
            logger, ct);

        var pairs = new List<CredentialPairCountDto>();
        try
        {
            var query = new QueryDefinition(
                "SELECT c.credentials.username AS username, c.credentials.password AS password, COUNT(1) AS count FROM c " +
                "WHERE IS_DEFINED(c.credentials.username) AND IS_DEFINED(c.credentials.password) " +
                "GROUP BY c.credentials.username, c.credentials.password");
            using var iterator = container.GetItemQueryIterator<PairRow>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                foreach (var r in page)
                {
                    pairs.Add(new CredentialPairCountDto
                    {
                        Username = r.Username ?? "",
                        Password = r.Password ?? "",
                        Count = r.Count,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stats/credentials: zapytanie par Cosmos nieudane.");
        }

        var dto = new CredentialStatsDto
        {
            TopUsernames = byUser.OrderByDescending(g => g.Count).Take(10)
                .Select(g => new UsernameCountDto { Username = g.Key ?? "", Count = g.Count }).ToList(),
            TopPasswords = byPass.OrderByDescending(g => g.Count).Take(10)
                .Select(g => new PasswordCountDto { Password = g.Key ?? "", Count = g.Count }).ToList(),
            TopPairs = pairs.OrderByDescending(p => p.Count).Take(10).ToList(),
            TotalAttempts = byUser.Sum(g => g.Count),
        };
        return Results.Json(dto, HoneyGridJson.Options);
    }

    // ── Pomocnicy ─────────────────────────────────────────────────────────
    private static Container GetEventsContainer(CosmosClient cosmos, IConfiguration config)
    {
        var databaseName = config["HoneyGrid:CosmosDatabase"] ?? "honeygrid";
        return cosmos.GetContainer(databaseName, "events");
    }

    private static async Task<long> ScalarAsync(
        Container container, QueryDefinition query, ILogger logger, CancellationToken ct)
    {
        try
        {
            using var iterator = container.GetItemQueryIterator<long>(query);
            if (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                foreach (var v in page) return v;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stats: zapytanie skalarne Cosmos nieudane: {Query}", query.QueryText);
        }
        return 0;
    }

    private static async Task<List<GroupRow>> GroupCountAsync(
        Container container, string sql, ILogger logger, CancellationToken ct)
    {
        var rows = new List<GroupRow>();
        try
        {
            using var iterator = container.GetItemQueryIterator<GroupRow>(new QueryDefinition(sql));
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                rows.AddRange(page);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stats: zapytanie grupujące Cosmos nieudane: {Sql}", sql);
        }
        return rows;
    }

    // Projekcje wyników Cosmos (kształt zgodny z aliasami w zapytaniach).
    private sealed record GroupRow
    {
        public string? Key { get; init; }
        public string? Label { get; init; }
        public long Count { get; init; }
    }

    private sealed record GeoRow
    {
        public string? Country { get; init; }
        public string? CountryName { get; init; }
        public double Lat { get; init; }
        public double Lon { get; init; }
        public long Count { get; init; }
    }

    private sealed record PairRow
    {
        public string? Username { get; init; }
        public string? Password { get; init; }
        public long Count { get; init; }
    }
}
