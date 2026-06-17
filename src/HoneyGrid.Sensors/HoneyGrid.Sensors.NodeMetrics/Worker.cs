using System.Diagnostics;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HoneyGrid.Contracts;

namespace HoneyGrid.Sensors.NodeMetrics;

public class Worker : BackgroundService
{
    private readonly CosmosClient _cosmos;
    private readonly ILogger<Worker> _logger;
    private readonly string _databaseName;
    private readonly string _nodeId;
    
    // Zmienne do liczenia rate'ów (użycie w czasie)
    private long _prevTotalJiffies = 0;
    private long _prevIdleJiffies = 0;
    private long _prevRxPackets = 0;
    private long _prevTxPackets = 0;
    private DateTimeOffset _prevTime = DateTimeOffset.MinValue;

    public Worker(CosmosClient cosmos, IConfiguration config, ILogger<Worker> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
        
        var location = config["NodeLocation"] ?? Environment.GetEnvironmentVariable("LOCATION") ?? "unknown";
        _nodeId = $"node-{location}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent NodeMetrics wystartował dla węzła SDN: {NodeId}", _nodeId);
        
        var container = _cosmos.GetContainer(_databaseName, "sdnNodes");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (cpu, ram, traffic, connections) = GetMetrics();
                
                var patch = new[]
                {
                    PatchOperation.Set("/cpu", cpu),
                    PatchOperation.Set("/ram", ram),
                    PatchOperation.Set("/filteredTraffic", traffic),
                    PatchOperation.Set("/connections", connections)
                };

                await container.PatchItemAsync<SdnNodeState>(_nodeId, new PartitionKey(_nodeId), patch, cancellationToken: stoppingToken);
                _logger.LogDebug("Wysłano metryki do CosmosDB dla węzła {NodeId}", _nodeId);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Dokument węzła {NodeId} jeszcze nie istnieje. Czekam, aż Ingestion go utworzy...", _nodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nie udało się zaktualizować metryk w CosmosDB.");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private (int cpu, int ram, int traffic, int connections) GetMetrics()
    {
        if (!OperatingSystem.IsLinux())
        {
            // Dummy na Windows dev
            return (40, 60, 15000, 1000);
        }

        int cpu = 0;
        int ram = 0;
        int traffic = 0;
        int connections = 0;

        try
        {
            // 1. CPU
            var statLines = File.ReadAllLines("/proc/stat");
            var cpuLine = statLines.FirstOrDefault(l => l.StartsWith("cpu "));
            if (cpuLine != null)
            {
                var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long user = long.Parse(parts[1]);
                long nice = long.Parse(parts[2]);
                long system = long.Parse(parts[3]);
                long idle = long.Parse(parts[4]);
                long iowait = long.Parse(parts[5]);
                long irq = long.Parse(parts[6]);
                long softirq = long.Parse(parts[7]);

                long totalIdle = idle + iowait;
                long totalNonIdle = user + nice + system + irq + softirq;
                long total = totalIdle + totalNonIdle;

                if (_prevTotalJiffies > 0)
                {
                    long totalDiff = total - _prevTotalJiffies;
                    long idleDiff = totalIdle - _prevIdleJiffies;
                    if (totalDiff > 0)
                    {
                        cpu = (int)((totalDiff - idleDiff) * 100 / totalDiff);
                    }
                }
                _prevTotalJiffies = total;
                _prevIdleJiffies = totalIdle;
            }

            // 2. RAM
            var memLines = File.ReadAllLines("/proc/meminfo");
            long totalMem = 0;
            long availableMem = 0;
            foreach (var line in memLines)
            {
                if (line.StartsWith("MemTotal:")) totalMem = ExtractKb(line);
                else if (line.StartsWith("MemAvailable:")) availableMem = ExtractKb(line);
            }
            if (totalMem > 0)
            {
                ram = (int)((totalMem - availableMem) * 100 / totalMem);
            }

            // 3. Traffic (pps)
            var now = DateTimeOffset.UtcNow;
            var devLines = File.ReadAllLines("/proc/net/dev");
            var ethLine = devLines.FirstOrDefault(l => l.TrimStart().StartsWith("eth0:")); // Zakładamy eth0
            if (ethLine != null)
            {
                var parts = ethLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long rxPackets = long.Parse(parts[2]);
                long txPackets = long.Parse(parts[10]);

                if (_prevTime != DateTimeOffset.MinValue)
                {
                    var seconds = (now - _prevTime).TotalSeconds;
                    if (seconds > 0)
                    {
                        traffic = (int)((rxPackets - _prevRxPackets + txPackets - _prevTxPackets) / seconds);
                    }
                }
                _prevRxPackets = rxPackets;
                _prevTxPackets = txPackets;
            }
            _prevTime = now;

            // 4. Połączenia TCP (ESTABLISHED)
            var tcpLines = File.ReadAllLines("/proc/net/tcp");
            connections = tcpLines.Count(l => l.Contains(" 01 ")); // 01 to stan ESTABLISHED

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd odczytu statystyk /proc");
        }

        return (cpu, ram, traffic, connections);
    }

    private long ExtractKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && long.TryParse(parts[1], out var val))
            return val;
        return 0;
    }
}
