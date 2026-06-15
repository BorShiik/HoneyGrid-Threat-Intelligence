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
            Id = "mcp-01", Name = "ThreatIntel Analyzer", Provider = "Cloudflare Workers AI",
            Status = "connected", Endpoint = "https://ti-analyzer.honeygrid.workers.dev",
            Tools = ["query_threat_logs", "enrich_ip", "classify_attack", "generate_ioc"],
            LastPing = 12, RequestsToday = 847,
        },
        new McpServerState
        {
            Id = "mcp-02", Name = "Sentinel Bridge", Provider = "Azure Functions",
            Status = "connected", Endpoint = "https://hg-sentinel-bridge.azurewebsites.net/mcp",
            Tools = ["create_incident", "update_watchlist", "run_kql_query"],
            LastPing = 34, RequestsToday = 312,
        },
        new McpServerState
        {
            Id = "mcp-03", Name = "Actor Profiler", Provider = "Azure OpenAI (gpt-4o-mini)",
            Status = "connected", Endpoint = "https://hg-ai-profiler.openai.azure.com",
            Tools = ["build_actor_dossier", "cluster_sessions", "assess_sophistication"],
            LastPing = 89, RequestsToday = 156,
        },
        new McpServerState
        {
            Id = "mcp-04", Name = "OSINT Enrichment", Provider = "Self-hosted",
            Status = "disconnected", Endpoint = "https://osint.internal.honeygrid.net/v1",
            Tools = ["whois_lookup", "dns_history", "certificate_transparency"],
            LastPing = -1, RequestsToday = 0,
        }
    };

    [Function(nameof(GetMcpServers))]
    public async Task<HttpResponseData> GetMcpServers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/mcp/servers")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(DefaultServers, cancellationToken);
        return response;
    }
}
