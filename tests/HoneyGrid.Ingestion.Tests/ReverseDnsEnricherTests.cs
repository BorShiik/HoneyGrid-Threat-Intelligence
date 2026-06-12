using HoneyGrid.Ingestion.Enrichment;
using Microsoft.Extensions.Logging.Abstractions;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>rDNS: błędy i timeouty pomijane po cichu, negatywne wyniki cache'owane.</summary>
public sealed class ReverseDnsEnricherTests
{
    /// <summary>Resolver zliczający wywołania o programowalnym zachowaniu.</summary>
    private sealed class FakeResolver(Func<string, CancellationToken, Task<string?>> resolve) : IReverseDnsResolver
    {
        public int CallCount { get; private set; }

        public Task<string?> ResolveAsync(string ip, CancellationToken ct)
        {
            CallCount++;
            return resolve(ip, ct);
        }
    }

    private static ReverseDnsEnricher CreateEnricher(IReverseDnsResolver resolver, Action<IngestionOptions>? configure = null) =>
        new(
            resolver,
            TestHelpers.Options(configure),
            TestHelpers.NewCache(),
            NullLogger<ReverseDnsEnricher>.Instance);

    [Fact]
    public async Task ResolverThrows_EventUnchanged_NoException()
    {
        var resolver = new FakeResolver((_, _) =>
            throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound));
        var enricher = CreateEnricher(resolver);

        var evt = TestHelpers.SampleEvent();
        var result = await enricher.EnrichAsync(evt, CancellationToken.None);

        Assert.Same(evt, result);
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task ResolverHangs_TimeoutCancelsLookup_EventUnchanged()
    {
        // Resolver "wisi" do anulowania — symulacja wolnego DNS bez rekordu PTR.
        var resolver = new FakeResolver(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "nigdy";
        });
        var enricher = CreateEnricher(resolver, o => o.RdnsTimeoutMs = 50);

        var evt = TestHelpers.SampleEvent();
        var result = await enricher.EnrichAsync(evt, CancellationToken.None);

        Assert.Same(evt, result);
    }

    [Fact]
    public async Task NegativeResult_IsCached_ResolverCalledOnce()
    {
        var resolver = new FakeResolver((_, _) => Task.FromResult<string?>(null));
        var enricher = CreateEnricher(resolver);

        await enricher.EnrichAsync(TestHelpers.SampleEvent("192.0.2.9"), CancellationToken.None);
        await enricher.EnrichAsync(TestHelpers.SampleEvent("192.0.2.9"), CancellationToken.None);

        // Wynik negatywny też w cache'u — drugi lookup nie powinien się wykonać.
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task Disabled_ResolverNeverCalled()
    {
        var resolver = new FakeResolver((_, _) => Task.FromResult<string?>("ptr.example.net"));
        var enricher = CreateEnricher(resolver, o => o.EnableReverseDns = false);

        var evt = TestHelpers.SampleEvent();
        var result = await enricher.EnrichAsync(evt, CancellationToken.None);

        Assert.Same(evt, result);
        Assert.Equal(0, resolver.CallCount);
    }
}
