using HoneyGrid.Ingestion;
using Serilog;

// HoneyGrid.Ingestion — pipeline ingestii zdarzeń.
// Konsumuje zdarzenia z Event Hub, wzbogaca je (GeoIP, threat intel)
// i zapisuje do Cosmos DB / Blob Storage.

var builder = Host.CreateApplicationBuilder(args);

// Serilog jako główny logger hosta.
// TODO (Track B, Tydzień 3): dodać sink konsolowy + Application Insights (Serilog.Sinks.*).
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .CreateLogger();

builder.Services.AddSerilog();

// TODO (Track B, Tydzień 3): rejestracja EventProcessorClient (Azure.Messaging.EventHubs.Processor)
//                            z checkpointami w Blob Storage.
// TODO (Track B, Tydzień 4): rejestracja serwisów wzbogacania: IGeoIpEnricher, IThreatIntelEnricher.
builder.Services.AddHostedService<EventHubIngestionWorker>();

var host = builder.Build();
host.Run();
