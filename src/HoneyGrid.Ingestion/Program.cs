using HoneyGrid.Ingestion;
using HoneyGrid.Ingestion.Enrichment;
using HoneyGrid.Ingestion.Sinks;
using Microsoft.Extensions.Options;

// =============================================================================
// HoneyGrid.Ingestion — worker ingestii i wzbogacania zdarzeń honeypotów.
//
// Przepływ: Event Hub (EventProcessorClient, checkpointy w Blob)
//   → pipeline wzbogacania (GeoIP → rDNS → AbuseIPDB)
//   → fan-out: Cosmos DB (hot), Blob "raw" (audyt), Service Bus "ai-classify" (Track B),
//              Microsoft Sentinel (Logs Ingestion API: DCE + DCR, tabela Cowrie_CL).
//
// Konfiguracja: sekcja "Ingestion" (env: Ingestion__<Nazwa> z Bicep).
// Uwierzytelnianie: wyłącznie DefaultAzureCredential (bezkluczowo).
// =============================================================================

var builder = Host.CreateApplicationBuilder(args);

// --- Opcje + walidacja przy starcie (fail fast na brakach konfiguracji z Bicep) ---
builder.Services.AddOptions<IngestionOptions>()
    .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<IngestionOptions>, IngestionOptionsValidator>();

// --- Cache wyników wzbogacania (GeoIP 1 h, rDNS 1 h, AbuseIPDB 6 h) ---
// SizeLimit wymusza Size=1 na każdym wpisie — chroni pamięć przy zalewie unikalnych IP.
builder.Services.AddMemoryCache(o => o.SizeLimit = 100_000);

// --- Klient HTTP dla AbuseIPDB ---
builder.Services.AddHttpClient(AbuseIpDbEnricher.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// --- Pipeline wzbogacania: kolejność rejestracji = kolejność wykonania ---
builder.Services.AddSingleton<IReverseDnsResolver, SystemReverseDnsResolver>();
builder.Services.AddSingleton<IEventEnricher, GeoIpEnricher>();
builder.Services.AddSingleton<IEventEnricher, ReverseDnsEnricher>();
builder.Services.AddSingleton<IEventEnricher, AbuseIpDbEnricher>();
builder.Services.AddSingleton<EnrichmentPipeline>();

// --- Sinki fan-outu ---
builder.Services.AddSingleton<IEventSinkTarget, CosmosEventWriter>();
builder.Services.AddSingleton<IEventSinkTarget, RawBlobWriter>();
// ServiceBusForwarder: singleton współdzielony jako sink i jako serwis w tle (pętla flush).
builder.Services.AddSingleton<ServiceBusForwarder>();
builder.Services.AddSingleton<IEventSinkTarget>(sp => sp.GetRequiredService<ServiceBusForwarder>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServiceBusForwarder>());
// SentinelLogsSink: jak wyżej — singleton współdzielony jako sink i serwis w tle.
// Sink opcjonalny: bez pary DCE/DCR (env Ingestion__DceLogsIngestionEndpoint +
// Ingestion__DcrImmutableId z Bicep) działa jako świadomy no-op.
builder.Services.AddSingleton<SentinelLogsSink>();
builder.Services.AddSingleton<IEventSinkTarget>(sp => sp.GetRequiredService<SentinelLogsSink>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SentinelLogsSink>());

// --- Główny worker ---
builder.Services.AddHostedService<IngestionWorker>();

var host = builder.Build();
host.Run();
