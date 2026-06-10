using HoneyGrid.Contracts;

// HoneyGrid.Sensors.Web — honeypot webowy (Minimal API).
// Udaje podatną aplikację (panel logowania WordPress / phpMyAdmin),
// rejestruje każde żądanie HTTP jako HoneypotEvent i wysyła do Event Hub.

var builder = WebApplication.CreateBuilder(args);

// TODO (Track A, Tydzień 2): rejestracja EventHubProducerClient + kanał (Channel<HoneypotEvent>)
//                            do asynchronicznej wysyłki zdarzeń.
// TODO (Track A, Tydzień 2): konfiguracja sensorId / sensorType z appsettings / zmiennych środowiskowych.

var app = builder.Build();

var sensorId = builder.Configuration["HoneyGrid:SensorId"] ?? "web-local-01";

// Endpoint zdrowia — używany przez sondy Container Apps / App Service.
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", sensorId }));

// Fałszywy panel logowania — klasyczny cel skanerów botnetów.
app.MapGet("/wp-login.php", (HttpContext ctx) =>
{
    // TODO (Track A, Tydzień 2): zwrócić realistyczny HTML panelu logowania WordPress.
    LogRequest(ctx);
    return Results.Content("<html><body><form method='post'>wp-login placeholder</form></body></html>", "text/html");
});

app.MapPost("/wp-login.php", (HttpContext ctx) =>
{
    // TODO (Track A, Tydzień 2): przechwycić poświadczenia z formularza (CredentialPair)
    //                            i opublikować zdarzenie login.failed do Event Hub.
    LogRequest(ctx);
    return Results.Unauthorized();
});

// Catch-all — każde inne żądanie też jest cennym sygnałem (skanowanie ścieżek).
app.MapFallback((HttpContext ctx) =>
{
    LogRequest(ctx);
    return Results.NotFound();
});

app.Run();

void LogRequest(HttpContext ctx)
{
    // Szkielet budowy zdarzenia — docelowo trafia do Event Hub, na razie tylko log.
    var evt = new HoneypotEvent
    {
        Id = Guid.NewGuid(),
        AttackerIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        SensorId = sensorId,
        SensorType = SensorType.Web,
        Timestamp = DateTimeOffset.UtcNow,
        EventType = EventType.HttpRequest,
        Http = new HttpInfo
        {
            Method = ctx.Request.Method,
            Path = ctx.Request.Path.Value,
            UserAgent = ctx.Request.Headers.UserAgent.ToString(),
        },
    };

    // TODO (Track A, Tydzień 3): wysyłka do Event Hub zamiast logowania lokalnego.
    app.Logger.LogInformation("Przechwycono żądanie: {Event}", HoneyGridJson.Serialize(evt));
}
