using HoneyGrid.Contracts;
using HoneyGrid.Sensors.Common;
using HoneyGrid.Sensors.Web;
using Microsoft.AspNetCore.HttpOverrides;

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
// adres proxy (zaobserwowane na żywych danych: 100.100.0.x, także jako ::ffff:100.100.0.x),
// a prawdziwy adres klienta niesie nagłówek X-Forwarded-For. Podsieci, z których ufamy
// temu nagłówkowi, są konfigurowalne: sekcja HoneyGrid:TrustedProxyNetworks
// (env: HoneyGrid__TrustedProxyNetworks__0, HoneyGrid__TrustedProxyNetworks__1, ...).
// Domyślnie: zakres wewnętrzny ingress-proxy Container Apps (zaobserwowany na żywych danych)
// + loopback dla testów lokalnych.
var trustedProxyNetworks = WebHoneypotLogic.ParseTrustedNetworks(
    builder.Configuration.GetSection("HoneyGrid:TrustedProxyNetworks").Get<string[]>()
    ?? ["100.100.0.0/16", "127.0.0.1/32", "::1/128"]);

// ForwardedHeadersMiddleware (kanoniczne podejście ASP.NET Core) przepisuje
// Connection.RemoteIpAddress na adres z X-Forwarded-For. Ochrona przed spoofingiem:
// atakujący może wysłać własny nagłówek X-Forwarded-For, dlatego middleware ufa mu
// TYLKO wtedy, gdy bezpośredni peer połączenia jest zaufanym proxy (KnownIPNetworks);
// w innym razie nagłówek jest ignorowany i zostaje RemoteIpAddress połączenia.
// ForwardLimit = 1: honorujemy wyłącznie OSTATNI wpis nagłówka — jedyny dopisany przez
// zaufany ingress; wpisy wcześniejsze (głębsze) są kontrolowane przez atakującego.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
foreach (var network in trustedProxyNetworks)
{
    forwardedHeadersOptions.KnownIPNetworks.Add(network);
}

// Middleware musi stać PRZED jakąkolwiek obsługą żądań, aby każda trasa-pułapka
// widziała już prawdziwy adres klienta.
app.UseForwardedHeaders(forwardedHeadersOptions);

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

// Prawdziwy adres atakującego: po ForwardedHeadersMiddleware RemoteIpAddress to już
// adres klienta (z X-Forwarded-For, gdy połączenie przyszło z zaufanego proxy) —
// tu zostaje tylko normalizacja zapisu (::ffff:1.2.3.4 → 1.2.3.4, null → "unknown").
string AttackerIp(HttpContext ctx)
    => WebHoneypotLogic.CanonicalIp(ctx.Connection.RemoteIpAddress);

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
