using Azure.Identity;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Sensors.NodeMetrics;

/// <summary>
/// Co <see cref="NodeMetricsOptions.IntervalSeconds"/> czyta /proc i PATCH-uje REALNE
/// metryki hosta (cpu/ram/ruch/połączenia + lastSeen) do dokumentu węzła SDN w Cosmos
/// (node-&lt;site&gt;). Bezkluczowo (DefaultAzureCredential). Gdy węzeł jeszcze nie istnieje
/// (brak zdarzeń od sensorów) — tworzy go z metrykami.
/// </summary>
public sealed class NodeMetricsWorker(
    IOptions<NodeMetricsOptions> options,
    ILogger<NodeMetricsWorker> logger) : BackgroundService
{
    private readonly NodeMetricsOptions _options = options.Value;
    private CosmosClient? _client;
    private Container? _container;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var site = _options.Site.ToLowerInvariant();
        var nodeId = $"node-{site}";
        var nodeName = string.IsNullOrWhiteSpace(_options.NodeName) ? $"Edge-{site}" : _options.NodeName!;
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.IntervalSeconds));

        logger.LogInformation(
            "NodeMetrics start: site={Site} node={Node} proc={Proc} interval={Sec}s",
            site, nodeId, _options.ProcPath, interval.TotalSeconds);

        if (string.IsNullOrWhiteSpace(_options.CosmosEndpoint))
        {
            logger.LogError("NodeMetrics: brak CosmosEndpoint — agent nie ma dokąd pisać, kończę.");
            return;
        }

        var container = GetContainer();

        // Migawki bazowe do liczenia różnic (CPU% i bajty/s liczymy między tickami).
        var prevCpu = ProcReader.ReadCpu(_options.ProcPath);
        var prevBytes = ProcReader.ReadNetBytes(_options.ProcPath);
        var prevTime = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTimeOffset.UtcNow;
            var curCpu = ProcReader.ReadCpu(_options.ProcPath);
            var curBytes = ProcReader.ReadNetBytes(_options.ProcPath);
            var elapsed = Math.Max(1.0, (now - prevTime).TotalSeconds);

            var cpuPct = ComputeCpuPercent(prevCpu, curCpu);
            var ramPct = ProcReader.ReadMemoryPercent(_options.ProcPath);
            var conns = ProcReader.ReadEstablishedConnections(_options.ProcPath);
            var trafficBps = curBytes >= prevBytes
                ? (int)Math.Min(int.MaxValue, (curBytes - prevBytes) / (ulong)elapsed)
                : 0;

            await PatchAsync(container, nodeId, nodeName, site, cpuPct, ramPct, trafficBps, conns, now, stoppingToken);

            prevCpu = curCpu;
            prevBytes = curBytes;
            prevTime = now;
        }
    }

    private static int ComputeCpuPercent(CpuSample? prev, CpuSample? cur)
    {
        if (prev is not { } p || cur is not { } c || c.Total <= p.Total)
        {
            return 0;
        }

        var totalDelta = c.Total - p.Total;
        var idleDelta = c.Idle >= p.Idle ? c.Idle - p.Idle : 0;
        var busy = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
        return Math.Clamp((int)Math.Round(100.0 * busy / totalDelta), 0, 100);
    }

    private async Task PatchAsync(
        Container container, string nodeId, string nodeName, string site,
        int cpu, int ram, int traffic, int conns, DateTimeOffset now, CancellationToken ct)
    {
        var patch = new[]
        {
            PatchOperation.Set("/cpu", cpu),
            PatchOperation.Set("/ram", ram),
            PatchOperation.Set("/filteredTraffic", traffic),
            PatchOperation.Set("/connections", conns),
            PatchOperation.Set("/lastSeen", now),
            PatchOperation.Set("/status", "active"),
        };

        try
        {
            await container.PatchItemAsync<SdnNodeState>(nodeId, new PartitionKey(nodeId), patch, cancellationToken: ct);
            logger.LogDebug("NodeMetrics {Node}: cpu={Cpu}% ram={Ram}% conn={Conn} bps={Bps}",
                nodeId, cpu, ram, conns, traffic);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Węzeł nieznany (żaden sensor jeszcze nie wysłał zdarzenia) — tworzymy z metrykami.
            var node = new SdnNodeState
            {
                Id = nodeId,
                Name = nodeName,
                Location = site,
                Status = "active",
                DynamicMigration = false,
                LastSeen = now,
                Cpu = cpu,
                Ram = ram,
                FilteredTraffic = traffic,
                Connections = conns,
            };
            try
            {
                await container.UpsertItemAsync(node, new PartitionKey(nodeId), cancellationToken: ct);
            }
            catch (Exception createEx)
            {
                logger.LogWarning(createEx, "NodeMetrics: nie udało się utworzyć węzła {Node}", nodeId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NodeMetrics: PATCH węzła {Node} nie powiódł się", nodeId);
        }
    }

    private Container GetContainer()
    {
        if (_container is not null)
        {
            return _container;
        }

        _client = new CosmosClient(
            _options.CosmosEndpoint,
            new DefaultAzureCredential(),
            new CosmosClientOptions { UseSystemTextJsonSerializerWithOptions = HoneyGridJson.Options });

        _container = _client.GetContainer(_options.CosmosDatabase, _options.SdnNodesContainer);
        return _container;
    }

    public override void Dispose()
    {
        _client?.Dispose();
        base.Dispose();
    }
}
