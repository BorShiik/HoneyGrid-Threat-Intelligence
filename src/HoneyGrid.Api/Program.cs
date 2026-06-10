using HoneyGrid.Api.Hubs;
using HoneyGrid.Contracts;

// HoneyGrid.Api — publiczne API platformy + SignalR (mapa ataków na żywo).

var builder = WebApplication.CreateBuilder(args);

// SignalR — strumień zdarzeń ataków do dashboardu (HoneyGrid.Web).
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        // Spójny format JSON z resztą platformy (camelCase, enumy jako stringi).
        options.PayloadSerializerOptions.PropertyNamingPolicy = HoneyGridJson.Options.PropertyNamingPolicy;
        options.PayloadSerializerOptions.DefaultIgnoreCondition = HoneyGridJson.Options.DefaultIgnoreCondition;
    });

builder.Services.AddHealthChecks();

// TODO (Track D, Tydzień 5): rejestracja repozytorium Cosmos DB (zapytania o zdarzenia/aktorów).
// TODO (Track D, Tydzień 6): uwierzytelnianie Entra ID + CORS dla HoneyGrid.Web.

var app = builder.Build();

// Endpoint zdrowia — sondy liveness/readiness.
app.MapHealthChecks("/health");

// TODO (Track D, Tydzień 5): REST: GET /api/events, GET /api/actors/{id}, GET /api/stats.
// TODO (Track D, Tydzień 7): GET /api/stix/bundle — eksport STIX 2.1 (HoneyGrid.Stix).
app.MapGet("/", () => Results.Ok(new { service = "HoneyGrid.Api", status = "ok" }));

// Hub SignalR — dashboard subskrybuje zdarzenia ataków w czasie rzeczywistym.
app.MapHub<AttackHub>("/hubs/attacks");

app.Run();
