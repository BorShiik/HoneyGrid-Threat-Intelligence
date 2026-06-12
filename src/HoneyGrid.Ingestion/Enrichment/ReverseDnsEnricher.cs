using System.Net;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Ingestion.Enrichment;

/// <summary>
/// Abstrakcja odwrotnego DNS — wstrzykiwana, aby testy mogły podstawić
/// resolver deterministyczny (bez prawdziwych zapytań sieciowych).
/// </summary>
public interface IReverseDnsResolver
{
    /// <summary>Zwraca nazwę hosta (rekord PTR) lub null, gdy brak wpisu.</summary>
    Task<string?> ResolveAsync(string ip, CancellationToken ct);
}

/// <summary>Domyślny resolver oparty o systemowy DNS.</summary>
public sealed class SystemReverseDnsResolver : IReverseDnsResolver
{
    public async Task<string?> ResolveAsync(string ip, CancellationToken ct)
    {
        var entry = await Dns.GetHostEntryAsync(ip, ct);
        return string.IsNullOrWhiteSpace(entry.HostName) ? null : entry.HostName;
    }
}

/// <summary>
/// Wzbogacanie o odwrotny DNS (PTR) adresu atakującego.
/// Większość IP atakujących nie ma rekordu PTR — błędy i timeouty pomijamy po cichu,
/// a wyniki (również NEGATYWNE — null) cache'ujemy, żeby nie odpytywać DNS w kółko.
///
/// TODO (kontrakt): HoneypotEvent nie ma obecnie pola na rDNS (np. "attackerHostname").
/// Kontraktu NIE modyfikujemy w tym tygodniu — enricher wykonuje i cache'uje lookup,
/// ale wyniku jeszcze nigdzie nie zapisuje. Po rozszerzeniu kontraktu wystarczy
/// dodać tutaj `evt with { AttackerHostname = hostname }`.
/// </summary>
public sealed class ReverseDnsEnricher(
    IReverseDnsResolver resolver,
    IOptions<IngestionOptions> options,
    IMemoryCache cache,
    ILogger<ReverseDnsEnricher> logger) : IEventEnricher
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IngestionOptions _options = options.Value;

    public async ValueTask<HoneypotEvent> EnrichAsync(HoneypotEvent evt, CancellationToken ct)
    {
        if (!_options.EnableReverseDns)
        {
            return evt;
        }

        var cacheKey = $"rdns:{evt.AttackerIp}";

        if (!cache.TryGetValue(cacheKey, out string? _))
        {
            var hostname = await ResolveSafelyAsync(evt.AttackerIp, ct);

            // Cache'ujemy też wynik negatywny (null), aby nie ponawiać lookupów.
            using var entry = cache.CreateEntry(cacheKey);
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            entry.Size = 1;
            entry.Value = hostname;
        }

        // Brak pola w kontrakcie — patrz TODO w nagłówku klasy.
        return evt;
    }

    /// <summary>Lookup z limitem czasu; każdy błąd (brak PTR, timeout, sieć) => null.</summary>
    private async Task<string?> ResolveSafelyAsync(string ip, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.RdnsTimeoutMs);

        try
        {
            return await resolver.ResolveAsync(ip, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // zamykanie hosta
        }
        catch (Exception ex)
        {
            // Najczęstszy przypadek: SocketException "host not found" — to normalne.
            logger.LogTrace(ex, "rDNS dla {Ip} nie powiódł się (brak PTR / timeout).", ip);
            return null;
        }
    }
}
