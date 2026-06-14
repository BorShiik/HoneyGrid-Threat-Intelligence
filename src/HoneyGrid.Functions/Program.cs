using Azure.Identity;
using HoneyGrid.Contracts;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// HoneyGrid.Functions — funkcje Azure (.NET isolated worker), Track B.
// Zaimplementowane / planowane:
//  - ClassifyEvents (CosmosDBTrigger): klasyfikacja zdarzeń (Tydzień 3: stub,
//    Tydzień 5: Azure OpenAI). [ZROBIONE — stub]
//  - FanOutToSignalR (CosmosDBTrigger): broadcast zdarzeń do dashboardu. [Tydzień 2]
//  - BuildAggregates (TimerTrigger): predrachowane agregaty overview/geo/credentials. [Tydzień 4]
//  - CorrelateActors (TimerTrigger): profilowanie aktorów + dossier AI. [Tydzień 5–6]
//
// Dostęp do Cosmos BEZKLUCZOWO: DefaultAzureCredential (lokalnie `az login`,
// w chmurze Managed Identity). Endpoint z ustawienia CosmosConnection:accountEndpoint
// (to samo, którego używa identity-based wyzwalacz CosmosDBTrigger).

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Klient Cosmos do operacji PATCH (klasyfikacja). Serializacja zgodna z kontraktem
// platformy (camelCase, enumy jako stringi) — kluczowe, by zapisać poprawny kształt.
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["CosmosConnection:accountEndpoint"]
        ?? config["HoneyGrid:CosmosEndpoint"]
        ?? throw new InvalidOperationException(
            "Brak ustawienia 'CosmosConnection:accountEndpoint' (endpoint konta Cosmos).");

    return new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions
    {
        UseSystemTextJsonSerializerWithOptions = HoneyGridJson.Options,
    });
});

builder.Build().Run();
