using HoneyGrid.Ingestion.Sinks;
using Microsoft.Extensions.Logging.Abstractions;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>
/// SentinelLogsSink jest OPCJONALNY: bez kompletnej pary DCE/DCR działa jako
/// świadomy no-op (żadnych połączeń z Azure, żadnych wyjątków). Testujemy
/// wyłącznie ścieżkę wyłączoną — wysyłka wymaga prawdziwego DCE i jest poza
/// zakresem testów jednostkowych (E2E w tygodniu 4).
/// </summary>
public sealed class SentinelLogsSinkTests
{
    private static SentinelLogsSink NewSink(Action<IngestionOptions>? configure = null) =>
        new(TestHelpers.Options(configure), NullLogger<SentinelLogsSink>.Instance);

    [Fact]
    public void Name_IsSentinelLogs()
    {
        Assert.Equal("SentinelLogs", NewSink().Name);
    }

    [Fact]
    public void IsEnabled_RequiresBothDceEndpointAndDcrImmutableId()
    {
        Assert.False(NewSink().IsEnabled); // oba puste (domyślne)
        Assert.False(NewSink(o => o.DceLogsIngestionEndpoint = "https://dce.example").IsEnabled);
        Assert.False(NewSink(o => o.DcrImmutableId = "dcr-abc").IsEnabled);
        Assert.True(NewSink(o =>
        {
            o.DceLogsIngestionEndpoint = "https://dce.example";
            o.DcrImmutableId = "dcr-abc";
        }).IsEnabled);
    }

    [Fact]
    public async Task DisabledSink_WriteAsyncDoesNotThrow()
    {
        // Pusty endpoint = sink wyłączony: zapis musi być cichym no-opem.
        var sink = NewSink();

        await sink.WriteAsync(TestHelpers.SampleEvent(), CancellationToken.None);
        await sink.WriteAsync(TestHelpers.SampleEvent("198.51.100.1"), CancellationToken.None);
    }

    [Fact]
    public async Task DisabledSink_StartWriteStop_NoUploadAttempted()
    {
        // Pełny cykl życia bez konfiguracji DCE/DCR: pętla tła ma drenować
        // i porzucać zdarzenia. Brak wyjątku = brak próby połączenia z Azure
        // (leniwy klient nigdy nie powstaje na ścieżce wyłączonej).
        var sink = NewSink();

        await sink.StartAsync(CancellationToken.None);
        await sink.WriteAsync(TestHelpers.SampleEvent(), CancellationToken.None);
        await sink.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DryRun_DisablesUploadEvenWithFullConfiguration()
    {
        // DryRun ma wyłączać wysyłkę nawet przy kompletnej parze DCE/DCR.
        var sink = NewSink(o =>
        {
            o.DryRun = true;
            o.DceLogsIngestionEndpoint = "https://dce.example";
            o.DcrImmutableId = "dcr-abc";
        });

        await sink.StartAsync(CancellationToken.None);
        await sink.WriteAsync(TestHelpers.SampleEvent(), CancellationToken.None);
        await sink.StopAsync(CancellationToken.None);
    }
}
