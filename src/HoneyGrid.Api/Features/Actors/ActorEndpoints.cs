using System.Text.Json.Serialization;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;

namespace HoneyGrid.Api.Features.Actors;

/// <summary>
/// Profile aktorów zagrożeń (Track B, killer-ficzy ③):
///  - <c>GET /api/actors</c>      — lista sprofilowanych aktorów,
///  - <c>GET /api/actors/{id}</c> — pojedynczy aktor (dossier).
///
/// Czyta z kontenera Cosmos <c>actors</c> (PK <c>/id</c>) zasilanego procesorem
/// korelacji (CorrelateActors, Tydzień 5–6). Kształt zgodny z frontendowym typem
/// ThreatActor w HoneyGrid.Web/src/types/api.ts.
/// </summary>
public static class ActorEndpoints
{
    public static IEndpointRouteBuilder MapActorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/actors", ListAsync).WithName("ListActors").WithTags("Actors");
        app.MapGet("/api/actors/{id}", GetAsync).WithName("GetActor").WithTags("Actors");
        return app;
    }

    private static async Task<IResult> ListAsync(
        CosmosClient cosmos,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HoneyGrid.Api.Actors");
        var actors = new List<ThreatActorDto>();
        try
        {
            var container = GetContainer(cosmos, config);
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.eventCount DESC");
            using var iterator = container.GetItemQueryIterator<ThreatActorDto>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                actors.AddRange(page);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Actors: lista — zapytanie Cosmos nieudane, zwracam pustą listę.");
        }

        return Results.Json(actors, HoneyGridJson.Options);
    }

    private static async Task<IResult> GetAsync(
        CosmosClient cosmos,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        string id,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HoneyGrid.Api.Actors");
        try
        {
            var container = GetContainer(cosmos, config);
            var response = await container.ReadItemAsync<ThreatActorDto>(id, new PartitionKey(id), cancellationToken: ct);
            return Results.Json(response.Resource, HoneyGridJson.Options);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { title = "Nie znaleziono aktora", id });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Actors: odczyt {Id} nieudany.", id);
            return Results.NotFound(new { title = "Nie znaleziono aktora", id });
        }
    }

    private static Container GetContainer(CosmosClient cosmos, IConfiguration config)
    {
        var databaseName = config["HoneyGrid:CosmosDatabase"] ?? "honeygrid";
        return cosmos.GetContainer(databaseName, "actors");
    }
}

/// <summary>Kontrakt aktora dla dashboardu (zgodny z frontendowym ThreatActor).</summary>
public sealed record ThreatActorDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("firstSeen")]
    public DateTimeOffset FirstSeen { get; init; }

    [JsonPropertyName("lastSeen")]
    public DateTimeOffset LastSeen { get; init; }

    [JsonPropertyName("eventCount")]
    public long EventCount { get; init; }

    [JsonPropertyName("knownIps")]
    public IReadOnlyList<string> KnownIps { get; init; } = [];

    [JsonPropertyName("countries")]
    public IReadOnlyList<string> Countries { get; init; } = [];

    /// <summary>minimal | intermediate | advanced.</summary>
    [JsonPropertyName("sophistication")]
    public string Sophistication { get; init; } = "minimal";

    /// <summary>opportunistic | targeted | automated.</summary>
    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "opportunistic";

    /// <summary>critical | high | medium | low.</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "low";

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
