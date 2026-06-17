using System.Net;
using System.Text.Json;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

public sealed class SdnController
{
    private readonly CosmosClient _cosmos;
    private readonly ILogger<SdnController> _logger;
    private readonly string _databaseName;

    // Brak danych mockowych: węzły pochodzą wyłącznie z kontenera sdnNodes,
    // który naplniają realne sensory (CosmosSdnNodeWriter) i agent NodeMetrics.

    public SdnController(CosmosClient cosmos, IConfiguration config, ILogger<SdnController> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
    }

    [Function(nameof(GetSdnNodes))]
    public async Task<HttpResponseData> GetSdnNodes(
        // Bez prefiksu 'api/' — host sam dokłada 'api' → /api/sdn/nodes.
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sdn/nodes")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var container = _cosmos.GetContainer(_databaseName, "sdnNodes");

        var nodes = new List<SdnNodeState>();
        try
        {
            var query = new QueryDefinition("SELECT * FROM c");
            using var iterator = container.GetItemQueryIterator<SdnNodeState>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                nodes.AddRange(page);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Kontener jeszcze nie istnieje
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var node in nodes)
        {
            if (node.LastSeen.HasValue && (now - node.LastSeen.Value) > TimeSpan.FromHours(1))
            {
                node.Status = "offline";
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(nodes, cancellationToken);
        return response;
    }

    [Function(nameof(ToggleMigration))]
    public async Task<HttpResponseData> ToggleMigration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sdn/nodes/{id}/migration")] HttpRequestData req,
        string id,
        CancellationToken cancellationToken)
    {
        var container = _cosmos.GetContainer(_databaseName, "sdnNodes");

        try
        {
            var response = await container.ReadItemAsync<SdnNodeState>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            var node = response.Resource;
            
            node.DynamicMigration = !node.DynamicMigration;
            
            await container.UpsertItemAsync(node, new PartitionKey(id), cancellationToken: cancellationToken);
            
            var httpResponse = req.CreateResponse(HttpStatusCode.OK);
            await httpResponse.WriteAsJsonAsync(node, cancellationToken);
            return httpResponse;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }
    }
}
