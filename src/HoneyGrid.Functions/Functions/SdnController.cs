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

    // Default mock nodes if DB is empty
    private static readonly List<SdnNodeState> DefaultNodes = new()
    {
        new SdnNodeState { Id = "sdn-01", Name = "Edge-WEU-01", Location = "frankfurt", Status = "active", DynamicMigration = true },
        new SdnNodeState { Id = "sdn-02", Name = "Edge-WEU-02", Location = "amsterdam", Status = "active", DynamicMigration = false },
        new SdnNodeState { Id = "sdn-03", Name = "Core-NEU-01", Location = "dublin", Status = "active", DynamicMigration = true },
        new SdnNodeState { Id = "sdn-04", Name = "Edge-EUS-01", Location = "virginia", Status = "degraded", DynamicMigration = true },
        new SdnNodeState { Id = "sdn-05", Name = "Edge-SEA-01", Location = "singapore", Status = "active", DynamicMigration = false },
        new SdnNodeState { Id = "sdn-06", Name = "Core-WUS-01", Location = "seattle", Status = "offline", DynamicMigration = false },
    };

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
        var db = _cosmos.GetDatabase(_databaseName);
        var container = await db.CreateContainerIfNotExistsAsync("sdnNodes", "/id", cancellationToken: cancellationToken);

        var nodes = new List<SdnNodeState>();
        var query = new QueryDefinition("SELECT * FROM c");
        using var iterator = container.Container.GetItemQueryIterator<SdnNodeState>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            nodes.AddRange(page);
        }

        if (nodes.Count == 0)
        {
            // Initialize DB with default nodes
            foreach (var node in DefaultNodes)
            {
                await container.Container.UpsertItemAsync(node, new PartitionKey(node.Id), cancellationToken: cancellationToken);
                nodes.Add(node);
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
