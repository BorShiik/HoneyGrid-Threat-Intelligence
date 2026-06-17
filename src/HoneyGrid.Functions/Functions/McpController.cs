using System.Net;
using HoneyGrid.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HoneyGrid.Functions.Functions;

public sealed class McpController
{
    private static readonly List<McpServerState> DefaultServers = new()
    {
        new McpServerState
        {
            Id = "mcp-01", Name = "HoneyGrid AI Classifier", Provider = "Azure OpenAI",
            Status = "connected", Endpoint = "internal://ServiceBus/attacks-topic",
            Tools = ["classify_events_batch"],
            LastPing = 12, RequestsToday = 0,
        }
    };

    [Function(nameof(GetMcpServers))]
    public async Task<HttpResponseData> GetMcpServers(
        // Route bez prefiksu 'api/' — host Functions sam dokłada prefix 'api'
        // (host.json domyślny), więc finalna ścieżka to /api/mcp/servers.
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mcp/servers")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(DefaultServers, cancellationToken);
        return response;
    }
}
