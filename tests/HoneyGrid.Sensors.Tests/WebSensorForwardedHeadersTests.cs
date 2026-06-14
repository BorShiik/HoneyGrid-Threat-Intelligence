using System.Net;
using HoneyGrid.Contracts;
using HoneyGrid.Sensors.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HoneyGrid.Sensors.Tests;

/// <summary>
/// Testy integracyjne potoku ForwardedHeadersMiddleware w sensorze webowym
/// (WebApplicationFactory na prawdziwym Program.cs): nagłówek X-Forwarded-For
/// jest honorowany TYLKO gdy bezpośredni peer jest zaufanym proxy (KnownIPNetworks),
/// a przy ForwardLimit = 1 liczy się wyłącznie ostatni wpis nagłówka.
///
/// TestServer nie ma prawdziwego gniazda, więc adres peera podstawiamy middleware'em
/// zarejestrowanym przez IStartupFilter — wykonuje się on PRZED UseForwardedHeaders
/// z Program.cs. Zdarzenia przechwytuje fałszywy IEventSink podmieniony w kontenerze DI.
/// </summary>
public sealed class WebSensorForwardedHeadersTests
{
    /// <summary>Sink zbierający zdarzenia w pamięci zamiast wysyłki do Event Hub.</summary>
    private sealed class CapturingSink : IEventSink
    {
        private readonly List<HoneypotEvent> _events = [];

        public IReadOnlyList<HoneypotEvent> Events
        {
            get { lock (_events) { return [.. _events]; } }
        }

        public ValueTask EnqueueAsync(HoneypotEvent evt, CancellationToken cancellationToken = default)
        {
            lock (_events) { _events.Add(evt); }
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Podstawia zadany adres peera w Connection.RemoteIpAddress na samym początku potoku —
    /// IStartupFilter z rejestracji testowej owija potok aplikacji, więc ten middleware
    /// wykona się przed UseForwardedHeaders.
    /// </summary>
    private sealed class FakeRemoteIpStartupFilter(IPAddress remoteIp) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (ctx, nextMiddleware) =>
                {
                    ctx.Connection.RemoteIpAddress = remoteIp;
                    await nextMiddleware();
                });
                next(app);
            };
    }

    /// <summary>
    /// Uruchamia sensor z podstawionym adresem peera, wysyła GET /.env z opcjonalnym
    /// X-Forwarded-For i zwraca AttackerIp z jedynego przechwyconego zdarzenia.
    /// </summary>
    private static async Task<string> ResolvedAttackerIpAsync(string remoteIp, string? forwardedFor)
    {
        var sink = new CapturingSink();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new FakeRemoteIpStartupFilter(IPAddress.Parse(remoteIp)));
                // Ostatnia rejestracja IEventSink wygrywa — zastępuje ChannelEventSink.
                services.AddSingleton<IEventSink>(sink);
            }));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.env");
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var evt = Assert.Single(sink.Events);
        return evt.AttackerIp;
    }

    [Fact]
    public async Task XFF_z_zaufanego_proxy_daje_adres_klienta()
    {
        // Peer = ingress Container Apps (100.100.0.0/16 z appsettings.json) → nagłówek honorowany.
        var ip = await ResolvedAttackerIpAsync("100.100.0.18", "203.0.113.7");

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public async Task XFF_od_niezaufanego_peera_jest_ignorowany()
    {
        // Bezpośrednie połączenie z internetu z podstawionym nagłówkiem — wierzymy
        // wyłącznie adresowi połączenia (ochrona przed spoofingiem).
        var ip = await ResolvedAttackerIpAsync("203.0.113.50", "6.6.6.6");

        Assert.Equal("203.0.113.50", ip);
    }

    [Fact]
    public async Task Wielokrotny_XFF_przy_ForwardLimit_1_honoruje_tylko_ostatni_wpis()
    {
        // Atakujący wysłał "6.6.6.6", envoy dopisał prawdziwy adres na końcu —
        // ForwardLimit = 1 przetwarza tylko wpis dopisany przez zaufany hop.
        var ip = await ResolvedAttackerIpAsync("100.100.0.18", "6.6.6.6, 198.51.100.9");

        Assert.Equal("198.51.100.9", ip);
    }

    [Fact]
    public async Task Zaufane_proxy_w_postaci_IPv4_mapped_tez_jest_rozpoznawane()
    {
        // Na żywych danych ingress pojawiał się również jako ::ffff:100.100.0.x.
        var ip = await ResolvedAttackerIpAsync("::ffff:100.100.0.114", "203.0.113.7");

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public async Task Brak_XFF_daje_adres_polaczenia_bez_prefiksu_ffff()
    {
        // Brak nagłówka (np. sonda wewnętrzna) — zostaje adres połączenia w formie kanonicznej.
        var ip = await ResolvedAttackerIpAsync("::ffff:100.100.0.18", forwardedFor: null);

        Assert.Equal("100.100.0.18", ip);
    }

    [Fact]
    public async Task Klient_IPv6_w_XFF_zostaje_zapisany_jako_IPv6()
    {
        var ip = await ResolvedAttackerIpAsync("100.100.0.18", "2001:db8::1");

        Assert.Equal("2001:db8::1", ip);
    }
}
