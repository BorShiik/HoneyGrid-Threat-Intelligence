using HoneyGrid.Sensors.NodeMetrics;

// =============================================================================
// HoneyGrid.Sensors.NodeMetrics — agent realnej telemetrii hosta VPS dla mapy SDN.
// Czyta /proc i bezkluczowo PATCH-uje cpu/ram/ruch/połączenia do węzła w Cosmos
// (kontener sdnNodes, id "node-<site>"). Konfiguracja: sekcja "NodeMetrics"
// (env NodeMetrics__Site, NodeMetrics__CosmosEndpoint, ...). Uruchamiany jako
// kontener obok sensorów na każdym VPS (AZURE_CLIENT_ID = tożsamość id-sensor).
// =============================================================================

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<NodeMetricsOptions>()
    .Bind(builder.Configuration.GetSection(NodeMetricsOptions.SectionName));

builder.Services.AddHostedService<NodeMetricsWorker>();

var host = builder.Build();
host.Run();
