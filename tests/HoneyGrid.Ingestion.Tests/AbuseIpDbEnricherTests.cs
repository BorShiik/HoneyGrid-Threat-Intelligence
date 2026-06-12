using HoneyGrid.Ingestion.Enrichment;
using Microsoft.Extensions.Logging.Abstractions;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>Mapowanie odpowiedzi AbuseIPDB na ThreatIntelInfo + odporność na błędy.</summary>
public sealed class AbuseIpDbEnricherTests
{
    private static AbuseIpDbEnricher CreateEnricher(FakeHttpMessageHandler handler, string apiKey = "test-key") =>
        new(
            new FakeHttpClientFactory(handler),
            TestHelpers.Options(o => o.AbuseIpDbApiKey = apiKey),
            TestHelpers.NewCache(),
            NullLogger<AbuseIpDbEnricher>.Instance);

    private static string CannedJson(int score) =>
        """{"data":{"ipAddress":"203.0.113.7","abuseConfidenceScore":SCORE,"countryCode":"PL","totalReports":42}}"""
            .Replace("SCORE", score.ToString());

    [Fact]
    public async Task EmptyApiKey_EnricherIsNoOp_HandlerNeverCalled()
    {
        var handler = FakeHttpMessageHandler.RespondingWithJson(CannedJson(99));
        var enricher = CreateEnricher(handler, apiKey: "");

        Assert.False(enricher.IsEnabled);

        var evt = TestHelpers.SampleEvent();
        var result = await enricher.EnrichAsync(evt, CancellationToken.None);

        Assert.Same(evt, result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task HighScore_MapsToKnownMaliciousThreatIntel()
    {
        var handler = FakeHttpMessageHandler.RespondingWithJson(CannedJson(75));
        var enricher = CreateEnricher(handler);

        var result = await enricher.EnrichAsync(TestHelpers.SampleEvent(), CancellationToken.None);

        Assert.NotNull(result.ThreatIntel);
        Assert.Equal(75, result.ThreatIntel!.Score);
        Assert.True(result.ThreatIntel.KnownMalicious);
        Assert.Equal(["AbuseIPDB"], result.ThreatIntel.Sources);
        // Klucz API i nagłówek Accept muszą trafić do żądania.
        Assert.Equal("test-key", handler.LastRequest!.Headers.GetValues("Key").Single());
    }

    [Theory]
    [InlineData(49, false)] // tuż pod progiem
    [InlineData(50, true)]  // dokładnie próg
    [InlineData(0, false)]
    [InlineData(100, true)]
    public async Task MaliciousThreshold_IsScoreAtLeast50(int score, bool expectedMalicious)
    {
        var handler = FakeHttpMessageHandler.RespondingWithJson(CannedJson(score));
        var enricher = CreateEnricher(handler);

        var result = await enricher.EnrichAsync(TestHelpers.SampleEvent(), CancellationToken.None);

        Assert.Equal(expectedMalicious, result.ThreatIntel!.KnownMalicious);
        Assert.Equal(score, result.ThreatIntel.Score);
    }

    [Fact]
    public async Task HandlerThrows_EventUnchanged_NoException()
    {
        var handler = FakeHttpMessageHandler.Throwing();
        var enricher = CreateEnricher(handler);

        var evt = TestHelpers.SampleEvent();
        var result = await enricher.EnrichAsync(evt, CancellationToken.None);

        Assert.Same(evt, result);
        Assert.Null(result.ThreatIntel);
        // Polly: 1 próba + 3 ponowienia.
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task SecondEventSameIp_ServedFromCache_SingleHttpCall()
    {
        var handler = FakeHttpMessageHandler.RespondingWithJson(CannedJson(80));
        var enricher = CreateEnricher(handler);

        var first = await enricher.EnrichAsync(TestHelpers.SampleEvent("198.51.100.5"), CancellationToken.None);
        var second = await enricher.EnrichAsync(TestHelpers.SampleEvent("198.51.100.5"), CancellationToken.None);

        Assert.Equal(80, first.ThreatIntel!.Score);
        Assert.Equal(80, second.ThreatIntel!.Score);
        Assert.Equal(1, handler.CallCount); // drugi raz z cache'a
    }
}
