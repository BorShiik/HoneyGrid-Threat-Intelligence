using HoneyGrid.Ingestion.Enrichment;
using Microsoft.Extensions.Logging.Abstractions;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>Łagodna degradacja GeoIP: brak baz .mmdb nie może wywracać ingestii.</summary>
public sealed class GeoIpEnricherTests
{
    [Fact]
    public async Task MissingDatabases_EnricherIsNoOp_EventUnchanged()
    {
        // Ścieżki wskazują nieistniejące pliki — enricher ma się wyłączyć bez wyjątku.
        using var enricher = new GeoIpEnricher(
            TestHelpers.Options(o =>
            {
                o.GeoIpCityDbPath = "geoip/nie-ma-takiego-pliku-City.mmdb";
                o.GeoIpAsnDbPath = "geoip/nie-ma-takiego-pliku-ASN.mmdb";
            }),
            TestHelpers.NewCache(),
            NullLogger<GeoIpEnricher>.Instance);

        Assert.False(enricher.IsEnabled);

        var evt = TestHelpers.SampleEvent();
        var result = await enricher.EnrichAsync(evt, CancellationToken.None);

        // No-op: dokładnie ten sam obiekt, bez pola geo.
        Assert.Same(evt, result);
        Assert.Null(result.Geo);
    }

    [Fact]
    public async Task MissingDatabases_DoesNotThrow_ForManyEvents()
    {
        using var enricher = new GeoIpEnricher(
            TestHelpers.Options(o =>
            {
                o.GeoIpCityDbPath = "brak.mmdb";
                o.GeoIpAsnDbPath = "brak.mmdb";
            }),
            TestHelpers.NewCache(),
            NullLogger<GeoIpEnricher>.Instance);

        // Strumień zdarzeń z różnymi IP — żadne nie może rzucić.
        foreach (var ip in new[] { "1.2.3.4", "2001:db8::1", "nie-ip", "" })
        {
            var evt = TestHelpers.SampleEvent(ip);
            var result = await enricher.EnrichAsync(evt, CancellationToken.None);
            Assert.Same(evt, result);
        }
    }
}
