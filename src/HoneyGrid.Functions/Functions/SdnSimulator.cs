using System.Net;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

public sealed class SdnSimulator
{
    private readonly CosmosClient _cosmos;
    private readonly ILogger<SdnSimulator> _logger;
    private readonly string _databaseName;
    private static readonly Random Rnd = new();

    public SdnSimulator(CosmosClient cosmos, IConfiguration config, ILogger<SdnSimulator> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
    }

    [Function(nameof(SimulateSdnTelemetry))]
    [SignalROutput(HubName = "attacks")]
    public async Task<SignalRMessageAction[]> SimulateSdnTelemetry(
        [TimerTrigger("*/5 * * * * *")] TimerInfo timer,
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
        catch (CosmosException)
        {
            // Container might not exist yet
            return [];
        }

        if (nodes.Count == 0) return [];

        var events = new List<SdnTelemetryEvent>();

        foreach (var node in nodes)
        {
            if (node.Status == "offline")
            {
                events.Add(new SdnTelemetryEvent
                {
                    Id = node.Id,
                    Cpu = 0,
                    Ram = 12,
                    FilteredTraffic = 0,
                    Connections = 0
                });
                continue;
            }

            int baseTraffic = node.Id switch
            {
                "sdn-03" => 23000,
                "sdn-04" => 31000,
                "sdn-01" => 12000,
                "sdn-02" => 8000,
                _ => 6000
            };

            int baseCpu = node.Status == "degraded" ? 85 : 40;
            int baseConnections = baseTraffic / 15;

            events.Add(new SdnTelemetryEvent
            {
                Id = node.Id,
                Cpu = Math.Clamp(baseCpu + Rnd.Next(-10, 15), 5, 100),
                Ram = Math.Clamp(baseCpu + Rnd.Next(5, 25), 20, 95),
                FilteredTraffic = Math.Max(0, baseTraffic + Rnd.Next(-2000, 2000)),
                Connections = Math.Max(0, baseConnections + Rnd.Next(-100, 100))
            });
        }

        // Broadcast to SignalR client using "sdnTelemetry" target
        return [new SignalRMessageAction("sdnTelemetry", [events])];
    }
}
