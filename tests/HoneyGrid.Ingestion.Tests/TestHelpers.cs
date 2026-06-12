using System.Net;
using HoneyGrid.Contracts;
using HoneyGrid.Ingestion;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>Wspólne narzędzia testowe: zdarzenia przykładowe, fałszywy HTTP, cache.</summary>
internal static class TestHelpers
{
    /// <summary>Minimalne poprawne zdarzenie do testów enricherów.</summary>
    public static HoneypotEvent SampleEvent(string ip = "203.0.113.7") => new()
    {
        Id = Guid.NewGuid(),
        AttackerIp = ip,
        SensorId = "ssh-eu-01",
        SensorType = SensorType.Ssh,
        Timestamp = new DateTimeOffset(2026, 6, 11, 12, 30, 0, TimeSpan.Zero),
        EventType = EventType.LoginFailed,
        Credentials = new CredentialPair { Username = "root", Password = "admin123" },
    };

    /// <summary>Świeży cache pamięciowy z limitem rozmiaru (jak w produkcji).</summary>
    public static IMemoryCache NewCache() =>
        new MemoryCache(new MemoryCacheOptions { SizeLimit = 1_000 });

    /// <summary>Opcje ingestii opakowane w IOptions.</summary>
    public static IOptions<IngestionOptions> Options(Action<IngestionOptions>? configure = null)
    {
        var options = new IngestionOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }
}

/// <summary>Fałszywy handler HTTP zwracający zaprogramowaną odpowiedź lub rzucający wyjątek.</summary>
internal sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    public static FakeHttpMessageHandler RespondingWithJson(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    public static FakeHttpMessageHandler Throwing() =>
        new(_ => throw new HttpRequestException("symulowana awaria sieci"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(responder(request));
    }
}

/// <summary>Fałszywa fabryka HttpClient zwracająca klienta z podstawionym handlerem.</summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
