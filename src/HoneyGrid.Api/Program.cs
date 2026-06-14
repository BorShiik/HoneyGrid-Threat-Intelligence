using Azure.Identity;
using Azure.Storage.Blobs;
using HoneyGrid.Api.Features.Actors;
using HoneyGrid.Api.Features.Feed;
using HoneyGrid.Api.Features.Sessions;
using HoneyGrid.Api.Features.Stats;
using HoneyGrid.Api.Features.Stix;
using HoneyGrid.Api.Hubs;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;

// HoneyGrid.Api — publiczne API platformy + SignalR (mapa ataków na żywo) +
// (Tydzień 7, Track A) endpointy killer-ficzy: /api/iocs/stix oraz
// /api/sessions/{id}/replay. Dostęp do danych BEZKLUCZOWO (DefaultAzureCredential:
// lokalnie az login, w chmurze Managed Identity id-api — tylko do ODCZYTU).

var builder = WebApplication.CreateBuilder(args);

// --- SignalR — strumień zdarzeń ataków do dashboardu (HoneyGrid.Web) ---
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        // Spójny format JSON z resztą platformy (camelCase, enumy jako stringi).
        options.PayloadSerializerOptions.PropertyNamingPolicy = HoneyGridJson.Options.PropertyNamingPolicy;
        options.PayloadSerializerOptions.DefaultIgnoreCondition = HoneyGridJson.Options.DefaultIgnoreCondition;
    });

builder.Services.AddHealthChecks();

// --- Klienci danych: bezkluczowo, jeden DefaultAzureCredential dla całego hosta.
// Endpointy (StixEndpoints, SessionEndpoints) pobierają je z DI — nie tworzą same.
// Konfiguracja z sekcji "HoneyGrid" (env: HoneyGrid__CosmosEndpoint, __BlobServiceUri).
// Puste wartości (kompilacja/dev) => klient nierejestrowany; w chmurze wstrzykuje je Bicep.
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
// Wszystkie wymagają zarejestrowanego CosmosClient (tylko odczyt).
app.MapFeedEndpoints();
app.MapStatsEndpoints();
app.MapActorEndpoints();

// --- Tydzień 7, Track A: killer-ficzy ---
// STIX 2.1 / IoC feed (silnik HoneyGrid.Stix) — wymaga zarejestrowanego CosmosClient.
app.MapStixEndpoints();
// Session Replay (parser HoneyGrid.Replay) — wymaga CosmosClient + BlobServiceClient.
app.MapSessionEndpoints();

// Hub SignalR — dashboard subskrybuje zdarzenia ataków w czasie rzeczywistym.
app.MapHub<AttackHub>("/hubs/attacks");

app.Run();
