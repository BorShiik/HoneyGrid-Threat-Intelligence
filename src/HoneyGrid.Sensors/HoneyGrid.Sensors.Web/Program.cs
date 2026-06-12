using HoneyGrid.Contracts;
using HoneyGrid.Sensors.Common;
using HoneyGrid.Sensors.Web;

// HoneyGrid.Sensors.Web — honeypot webowy (Minimal API).
// Udaje podatną aplikację (panele logowania WordPress / phpMyAdmin, wyciekłe pliki .env / .git),
// rejestruje każde żądanie HTTP jako HoneypotEvent i wysyła do Event Hub poprzez IEventSink.

var builder = WebApplication.CreateBuilder(args);

// Bezkluczowy producent Event Hub (kanał + shipper + sink). sensorId/sensorType/namespace
// pochodzą z sekcji "HoneyGrid" appsettings.json lub zmiennych środowiskowych.
builder.AddHoneyGridEventHub();

var app = builder.Build();

// Identyfikator sensora z konfiguracji (do healthz i budowy zdarzeń).
var sensorId = builder.Configuration["HoneyGrid:SensorId"] ?? "web-local-01";
var sink = app.Services.GetRequiredService<IEventSink>();

// W Container Apps ruch przychodzi przez ingress (envoy) i RemoteIpAddress to wewnętrzny
// adres proxy (100.100.0.x), a prawdziwy adres klienta niesie nagłówek X-Forwarded-For.
// Podsieci, z których ufamy temu nagłówkowi, są konfigurowalne (HoneyGrid:TrustedProxyNetworks).
var trustedProxyNetworks = WebHoneypotLogic.ParseTrustedNetworks(
    builder.Configuration.GetSection("HoneyGrid:TrustedProxyNetworks").Get<string[]>()
    ?? ["100.100.0.0/16"]);

// Endpoint zdrowia — używany przez sondy Container Apps / App Service.
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", sensorId }));

// ---- Trasy-pułapki (bait) — typowe cele skanerów i botnetów ----

// Fałszywy panel logowania WordPress.
app.MapGet("/wp-login.php", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Content(DecoyContent.WpLoginHtml, "text/html");
});

// Przechwycenie poświadczeń z formularza WordPress → zdarzenie login.failed.
app.MapPost("/wp-login.php", async (HttpContext ctx) =>
{
    await EmitLoginAttempt(ctx);
    return Results.Unauthorized();
});

// Wyciekłe pliki konfiguracyjne (częste cele automatów).
app.MapGet("/.env", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Text(DecoyContent.DotEnv, "text/plain");
});

app.MapGet("/.git/config", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Text(DecoyContent.GitConfig, "text/plain");
});

app.MapGet("/.aws/credentials", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Text(DecoyContent.AwsCredentials, "text/plain");
});

// Panele administracyjne.
app.MapGet("/admin", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Content(DecoyContent.AdminHtml, "text/html");
});

app.MapPost("/admin", async (HttpContext ctx) =>
{
    await EmitLoginAttempt(ctx);
    return Results.Unauthorized();
});

app.MapGet("/phpmyadmin", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Content(DecoyContent.PhpMyAdminHtml, "text/html");
});

// Spring Boot Actuator — wyciek konfiguracji.
app.MapGet("/actuator/env", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Text(DecoyContent.ActuatorEnv, "application/json");
});

// Korzeń API — sugeruje istnienie chronionych zasobów.
app.MapGet("/api", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.Text(DecoyContent.ApiRoot, "application/json");
});

// Sondy .well-known (np. /.well-known/security.txt) — częsta rekonesansowa ścieżka.
app.MapGet("/.well-known/{**rest}", async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.NotFound();
});

// Generyczny POST (dowolna inna ścieżka) — też może nieść poświadczenia.
app.MapPost("/{**rest}", async (HttpContext ctx) =>
{
    await EmitLoginAttempt(ctx);
    return Results.NotFound();
});

// Catch-all — każde inne żądanie to cenny sygnał (skanowanie ścieżek).
app.MapFallback(async (HttpContext ctx) =>
{
    await EmitHttpRequest(ctx);
    return Results.NotFound();
});

app.Run();

// ---- Funkcje pomocnicze wiążące żądanie HTTP z IEventSink ----

// Prawdziwy adres atakującego: X-Forwarded-For gdy połączenie przyszło z zaufanego proxy,
// inaczej adres połączenia (zawsze bez prefiksu ::ffff:).
string AttackerIp(HttpContext ctx)
{
    var remote = ctx.Connection.RemoteIpAddress;
    return WebHoneypotLogic.ResolveAttackerIp(
        ctx.Request.Headers["X-Forwarded-For"].ToString(),
        remote?.ToString(),
        WebHoneypotLogic.IsTrustedProxy(remote, trustedProxyNetworks));
}

async Task EmitHttpRequest(HttpContext ctx)
{
    var evt = WebHoneypotLogic.BuildHttpRequestEvent(
        sensorId,
        AttackerIp(ctx),
        ctx.Request.Method,
        ctx.Request.Path.Value,
        ctx.Request.Headers.UserAgent.ToString(),
        DateTimeOffset.UtcNow);

    await sink.EnqueueAsync(evt, ctx.RequestAborted);
    app.Logger.LogInformation("Przechwycono żądanie {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
}

async Task EmitLoginAttempt(HttpContext ctx)
{
    CredentialPair? credentials = null;
    if (ctx.Request.HasFormContentType)
    {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        credentials = WebHoneypotLogic.ExtractCredentials(
            form.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value.ToString())));
    }

    var evt = WebHoneypotLogic.BuildLoginFailedEvent(
        sensorId,
        AttackerIp(ctx),
        ctx.Request.Path.Value,
        ctx.Request.Headers.UserAgent.ToString(),
        credentials,
        DateTimeOffset.UtcNow);

    await sink.EnqueueAsync(evt, ctx.RequestAborted);
    app.Logger.LogInformation(
        "Przechwycono próbę logowania: użytkownik={User}", credentials?.Username ?? "(brak)");
}

// Umożliwia testom integracyjnym (WebApplicationFactory) odwołanie się do klasy Program.
public partial class Program;
