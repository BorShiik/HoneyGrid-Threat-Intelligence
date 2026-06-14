using HoneyGrid.Stix;
using Microsoft.Azure.Cosmos;

namespace HoneyGrid.Api.Features.Stix;

/// <summary>
/// Endpoint eksportu STIX 2.1 — killer feature ④.
/// Mapuje dane z honeypotów (Cosmos) na poprawny bundle STIX 2.1 i udostępnia
/// pod <c>GET /api/iocs/stix</c> (opcjonalny filtr <c>?type=ip|hash|credential</c>).
///
/// Zasada defensywna: każdy błąd Cosmos → pusty bundle (sama Identity),
/// nigdy 500. Pusty zbiór danych również zwraca poprawny bundle z Identity.
/// </summary>
public static class StixEndpoints
{
    /// <summary>Rejestruje endpoint(y) STIX na routerze aplikacji.</summary>
    public static IEndpointRouteBuilder MapStixEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/iocs/stix", HandleAsync)
            .WithName("GetStixBundle")
            .WithTags("STIX");

        return app;
    }

    /// <summary>
    /// Buduje bundle STIX z indykatorów zebranych z kontenera 'events'.
    /// CosmosClient jest wstrzykiwany przez DI (rejestrowany przez orkiestratora).
    /// </summary>
    private static async Task<IResult> HandleAsync(
        CosmosClient cosmos,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        string? type,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("HoneyGrid.Api.Stix");
        var databaseName = config["HoneyGrid:CosmosDatabase"] ?? "honeygrid";

        var iocs = new List<StixIocInput>();
        try
        {
            var container = cosmos.GetContainer(databaseName, "events");
            await CollectIocsAsync(container, type, iocs, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            // Defensywnie: brak Cosmos / błąd zapytania → pusty bundle (sama Identity).
            logger.LogWarning(ex, "Eksport STIX: zapytanie do Cosmos nieudane, zwracam bundle z samą Identity.");
        }

        var bundle = StixExportEngine.Build(iocs);
        return Results.Content(StixSerializer.ToJson(bundle), "application/json");
    }

    /// <summary>
    /// Zbiera wskaźniki z kontenera 'events':
    ///  - DISTINCT złośliwe IP (threatIntel.knownMalicious lub threatIntel.score &gt;= 50),
    ///  - DISTINCT downloadHash,
    ///  - top pary poświadczeń (login).
    /// Filtr <paramref name="type"/> ogranicza zakres (ip | hash | credential).
    /// </summary>
    private static async Task CollectIocsAsync(
        Container container,
        string? type,
        List<StixIocInput> iocs,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var wantAll = string.IsNullOrWhiteSpace(type);
        var t = type?.Trim().ToLowerInvariant();

        // Złośliwe adresy IP.
        if (wantAll || t == "ip")
        {
            const string sql =
                "SELECT DISTINCT VALUE c.attackerIp FROM c " +
                "WHERE IS_DEFINED(c.attackerIp) AND " +
                "(c.threatIntel.knownMalicious = true OR c.threatIntel.score >= 50)";
            await foreach (var ip in QueryStringsAsync(container, sql, logger, cancellationToken))
            {
                iocs.Add(new StixIocInput { IocType = StixIocType.Ipv4, Value = ip });
            }
        }

        // Hashe pobranych artefaktów.
        if (wantAll || t == "hash")
        {
            const string sql =
                "SELECT DISTINCT VALUE c.downloadHash FROM c WHERE IS_DEFINED(c.downloadHash)";
            await foreach (var hash in QueryStringsAsync(container, sql, logger, cancellationToken))
            {
                iocs.Add(new StixIocInput { IocType = StixIocType.Sha256, Value = hash });
            }
        }

        // Pary poświadczeń (login) — wzorce brute-force → mapowane na MITRE T1110.
        if (wantAll || t == "credential")
        {
            const string sql =
                "SELECT DISTINCT VALUE c.credentials.username FROM c " +
                "WHERE IS_DEFINED(c.credentials.username) AND c.eventType IN ('login.failed', 'login.success')";
            await foreach (var login in QueryStringsAsync(container, sql, logger, cancellationToken))
            {
                iocs.Add(new StixIocInput
                {
                    IocType = StixIocType.CredentialPattern,
                    Value = login,
                    MitreTechnique = "T1110",
                });
            }
        }
    }

    /// <summary>Wykonuje zapytanie zwracające skalarne łańcuchy, pomijając wartości puste.</summary>
    private static async IAsyncEnumerable<string> QueryStringsAsync(
        Container container,
        string sql,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var iterator = container.GetItemQueryIterator<string>(new QueryDefinition(sql));
        while (iterator.HasMoreResults)
        {
            FeedResponse<string>? page = null;
            try
            {
                page = await iterator.ReadNextAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Defensywnie: pojedyncza nieudana strona nie wywraca eksportu.
                logger.LogWarning(ex, "Eksport STIX: błąd strony wyników Cosmos, pomijam.");
                yield break;
            }

            foreach (var value in page)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }
}
