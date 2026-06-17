using System.Net;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Stan serwerów MCP/AI dla strony "Integracje AI". Katalog serwerów jest
/// reprezentatywny (jeden realny: klasyfikator AI), ale <c>requestsToday</c> jest
/// PRAWDZIWY — liczba zdarzeń sklasyfikowanych dziś (pole classification w Cosmos),
/// czyli faktyczna liczba wywołań AI. Dziennik audytu (aiAuditLog) emituje na żywo
/// funkcja ClassifyEvents.
/// </summary>
public sealed class McpController(
    CosmosClient cosmos,
    IConfiguration configuration,
    ILogger<McpController> logger)
{
    [Function(nameof(GetMcpServers))]
    public async Task<HttpResponseData> GetMcpServers(
        // Route bez prefiksu 'api/' — host Functions sam dokłada prefix 'api'
        // (host.json domyślny), więc finalna ścieżka to /api/mcp/servers.
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mcp/servers")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var requestsToday = await CountClassifiedTodayAsync(cancellationToken);

        var servers = new List<McpServerState>
        {
            new()
            {
                Id = "mcp-01",
                Name = "HoneyGrid AI Classifier",
                Provider = "Azure OpenAI",
                Status = "connected",
                Endpoint = "internal://ServiceBus/attacks-topic",
                Tools = ["classify_events_batch"],
                LastPing = 12,
                RequestsToday = requestsToday,
            },
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(servers, cancellationToken);
        return response;
    }

    /// <summary>Liczba zdarzeń z polem classification od północy UTC (= realne wywołania AI dziś).</summary>
    private async Task<int> CountClassifiedTodayAsync(CancellationToken ct)
    {
        try
        {
            var database = configuration["CosmosDatabase"] ?? "honeygrid";
            var container = cosmos.GetContainer(database, "events");

            // timestamp przechowywany jako ISO-8601 → porównanie leksykograficzne z północą UTC.
            var startOfDay = DateTimeOffset.UtcNow.UtcDateTime.Date.ToString("yyyy-MM-ddTHH:mm:ss");
            var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE IS_DEFINED(c.classification) AND c.timestamp >= @start")
                .WithParameter("@start", startOfDay);

            using var iterator = container.GetItemQueryIterator<int>(query);
            var count = 0;
            while (iterator.HasMoreResults)
            {
                foreach (var n in await iterator.ReadNextAsync(ct))
                {
                    count += n;
                }
            }
            return count;
        }
        catch (CosmosException ex)
        {
            // Kontener/baza jeszcze nie istnieje albo brak uprawnień — zwracamy 0, bez zmyślania.
            logger.LogDebug(ex, "MCP: nie udało się policzyć dzisiejszych klasyfikacji.");
            return 0;
        }
    }
}
