using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using HoneyGrid.Api.Features.Actors;
using HoneyGrid.Api.Features.Feed;
using HoneyGrid.Api.Features.Sessions;
using HoneyGrid.Api.Features.Stats;
using HoneyGrid.Api.Features.Stix;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;

// HoneyGrid.Api — publiczne API platformy (REST, tylko ODCZYT, bezkluczowo).
// Realtime (mapa / lenta na żywo) NIE jest hostowany tutaj: w modelu Serverless
// rozsyłaniem zajmują się funkcje (FanOutToSignalR + negotiate), a klient łączy się
// bezpośrednio z Azure SignalR Service. Dostęp do danych przez DefaultAzureCredential
// (lokalnie `az login`, w chmurze Managed Identity id-api — least privilege, odczyt).

var builder = WebApplication.CreateBuilder(args);

// --- Observability: OpenTelemetry → Azure Monitor (Application Insights).
// Eksport włącza się, gdy ustawiono APPLICATIONINSIGHTS_CONNECTION_STRING;
// bez niej (lokalnie/dev) telemetryia po prostu nie jest wysyłana.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

builder.Services.AddHealthChecks();

// --- Klienci danych: bezkluczowo, jeden DefaultAzureCredential dla całego hosta.
// Endpointy pobierają klientów z DI — nie tworzą ich same. Konfiguracja z sekcji
// "HoneyGrid" (env: HoneyGrid__CosmosEndpoint, __BlobServiceUri). Puste wartości
// (kompilacja/dev) => klient nierejestrowany; w chmurze wstrzykuje je Bicep.
var cosmosEndpoint = builder.Configuration["HoneyGrid:CosmosEndpoint"];
var blobServiceUri = builder.Configuration["HoneyGrid:BlobServiceUri"];
var credential = new DefaultAzureCredential();

if (!string.IsNullOrWhiteSpace(cosmosEndpoint))
{
    builder.Services.AddSingleton(new CosmosClient(cosmosEndpoint, credential, new CosmosClientOptions
    {
        // Serializacja zgodna z kontraktem platformy (małe "id", "attackerIp").
        UseSystemTextJsonSerializerWithOptions = HoneyGridJson.Options,
    }));
}

if (!string.IsNullOrWhiteSpace(blobServiceUri))
{
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(blobServiceUri), credential));
}

// --- CORS dla dashboardu (Static Web App / dev) — tylko GET, bez kluczy ---
const string corsPolicy = "honeygrid-web";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .WithMethods("GET"));
});

var app = builder.Build();

app.UseCors(corsPolicy);

// Endpoint zdrowia — sondy liveness/readiness Container Apps.
app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Ok(new { service = "HoneyGrid.Api", status = "ok" }));

// --- Track B: API dashboardu (feed, statystyki, aktorzy) ---
app.MapFeedEndpoints();
app.MapStatsEndpoints();
app.MapActorEndpoints();

// --- Track A: killer-ficzy ---
// STIX 2.1 / IoC feed (silnik HoneyGrid.Stix) — wymaga zarejestrowanego CosmosClient.
app.MapStixEndpoints();
// Session Replay (parser HoneyGrid.Replay) — wymaga CosmosClient + BlobServiceClient.
app.MapSessionEndpoints();

app.Run();
