using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;

namespace HoneyGrid.Api.Features.Feed;

/// <summary>
/// Strumień historyczny/żywy zdarzeń — <c>GET /api/feed?since=&amp;take=</c>.
/// Track B (dashboard). Zwraca tablicę <see cref="HoneypotEvent"/> w kontrakcie
/// platformy (camelCase, enumy jako stringi). Realtime idzie osobno przez SignalR.
///
/// Zasada defensywna: każdy błąd Cosmos → pusta lista (nigdy 500), aby dashboard
/// pozostał użyteczny nawet przy chwilowej niedostępności danych.
/// </summary>
public static class FeedEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public static IEndpointRouteBuilder MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/feed", HandleAsync)
            .WithName("GetFeed")
            .WithTags("Feed");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        CosmosClient cosmos,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        string? since,
        int? take,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("HoneyGrid.Api.Feed");
        var databaseName = config["HoneyGrid:CosmosDatabase"] ?? "honeygrid";
        var limit = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        var events = new List<HoneypotEvent>(limit);
        try
        {
            var container = cosmos.GetContainer(databaseName, "events");

            // Cross-partition, newest-first; optional incremental cursor by timestamp.
            var query = new QueryDefinition(
                "SELECT TOP @take * FROM c " +
                (string.IsNullOrWhiteSpace(since) ? "" : "WHERE c.timestamp > @since ") +
                "ORDER BY c.timestamp DESC")
                .WithParameter("@take", limit);

            if (!string.IsNullOrWhiteSpace(since))
            {
                query = query.WithParameter("@since", since);
            }

            using var iterator = container.GetItemQueryIterator<HoneypotEvent>(query);
            while (iterator.HasMoreResults && events.Count < limit)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                events.AddRange(page);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Feed: zapytanie do Cosmos nieudane, zwracam pustą listę.");
        }

        return Results.Json(events, HoneyGridJson.Options);
    }
}
