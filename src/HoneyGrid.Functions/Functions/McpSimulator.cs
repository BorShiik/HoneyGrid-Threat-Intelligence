using HoneyGrid.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.SignalRService;

namespace HoneyGrid.Functions.Functions;

public sealed class McpSimulator
{
    private static readonly Random Rnd = new();
    private static int _seq = 0;

    private static readonly string[] Tools01 = ["query_threat_logs", "enrich_ip", "classify_attack", "generate_ioc"];
    private static readonly string[] Tools02 = ["create_incident", "update_watchlist", "run_kql_query"];
    private static readonly string[] Tools03 = ["build_actor_dossier", "cluster_sessions", "assess_sophistication"];

    [Function(nameof(SimulateMcpAudit))]
    [SignalROutput(HubName = "attacks")]
    public SignalRMessageAction[] SimulateMcpAudit(
        [TimerTrigger("*/3 * * * * *")] TimerInfo timer)
    {
        var burst = Rnd.Next(1, 3);
        var events = new List<AiAuditEntry>();

        for (var i = 0; i < burst; i++)
        {
            _seq++;
            var serverChoice = Rnd.Next(0, 3);
            
            string serverName = "";
            string tool = "";
            string input = "";

            if (serverChoice == 0)
            {
                serverName = "ThreatIntel Analyzer";
                tool = Tools01[Rnd.Next(Tools01.Length)];
                input = tool switch {
                    "classify_attack" => $"{{\"ip\":\"185.{Rnd.Next(100, 255)}.101.42\",\"type\":\"brute-force\"}}",
                    "enrich_ip" => $"{{\"ip\":\"{Rnd.Next(1, 255)}.{Rnd.Next(0, 255)}.{Rnd.Next(0, 255)}.{Rnd.Next(1, 255)}\"}}",
                    "generate_ioc" => $"{{\"hash\":\"sha256:{Guid.NewGuid().ToString().Replace("-", "")}\"}}",
                    _ => "{}"
                };
            }
            else if (serverChoice == 1)
            {
                serverName = "Sentinel Bridge";
                tool = Tools02[Rnd.Next(Tools02.Length)];
                input = tool switch {
                    "create_incident" => $"{{\"severity\":\"high\",\"title\":\"Mass brute-force\"}}",
                    "update_watchlist" => $"{{\"ip\":\"192.168.1.100\"}}",
                    _ => "{}"
                };
            }
            else
            {
                serverName = "Actor Profiler";
                tool = Tools03[Rnd.Next(Tools03.Length)];
                input = tool switch {
                    "build_actor_dossier" => $"{{\"actorId\":\"actor-{Guid.NewGuid().ToString()[..8]}\"}}",
                    "cluster_sessions" => $"{{\"timeframe\":\"24h\"}}",
                    _ => "{}"
                };
            }

            bool isError = Rnd.NextDouble() > 0.9;
            int latency = isError ? 0 : Rnd.Next(50, 1500);

            events.Add(new AiAuditEntry
            {
                Id = $"audit-{_seq}",
                Timestamp = DateTime.UtcNow.ToString("O"),
                Server = serverName,
                Tool = tool,
                Input = input,
                LatencyMs = latency,
                Status = isError ? "error" : "success"
            });
        }

        return [new SignalRMessageAction("aiAuditLog", [events])];
    }
}
