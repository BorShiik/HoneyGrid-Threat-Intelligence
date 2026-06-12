using HoneyGrid.Contracts;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Ingestion.Enrichment;

/// <summary>
/// Wzbogacanie geolokalizacyjne na lokalnych bazach MaxMind GeoLite2 (City + ASN).
///
/// ŁAGODNA DEGRADACJA: w momencie deployu nie mamy klucza licencyjnego MaxMind,
/// więc pliki .mmdb mogą nie istnieć — wtedy enricher loguje JEDNO ostrzeżenie
/// przy starcie i staje się no-opem (zdarzenia płyną dalej bez pola geo).
/// Użytkownik dogrywa bazy później wg src/HoneyGrid.Ingestion/geoip/README.md.
///
/// DatabaseReader jest wątkowo-bezpieczny — ładujemy raz na cały czas życia procesu.
/// Wyniki per IP trzymamy w IMemoryCache (TTL 1 h, wpisy z rozmiarem — cache ma SizeLimit).
/// </summary>
public sealed class GeoIpEnricher : IEventEnricher, IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly DatabaseReader? _cityReader;
    private readonly DatabaseReader? _asnReader;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeoIpEnricher> _logger;

    public GeoIpEnricher(
        IOptions<IngestionOptions> options,
        IMemoryCache cache,
        ILogger<GeoIpEnricher> logger)
    {
        _cache = cache;
        _logger = logger;

        _cityReader = TryOpenDatabase(options.Value.GeoIpCityDbPath, "GeoLite2-City");
        _asnReader = TryOpenDatabase(options.Value.GeoIpAsnDbPath, "GeoLite2-ASN");
    }

    /// <summary>Czy enricher ma załadowaną przynajmniej jedną bazę (do diagnostyki/testów).</summary>
    public bool IsEnabled => _cityReader is not null || _asnReader is not null;

    public ValueTask<HoneypotEvent> EnrichAsync(HoneypotEvent evt, CancellationToken ct)
    {
        // No-op: brak baz albo zdarzenie już zawiera geolokalizację (idempotencja).
        if (!IsEnabled || evt.Geo is not null)
        {
            return ValueTask.FromResult(evt);
        }

        var geo = _cache.GetOrCreate($"geo:{evt.AttackerIp}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            entry.Size = 1;
            return Lookup(evt.AttackerIp);
        });

        return ValueTask.FromResult(geo is null ? evt : evt with { Geo = geo });
    }

    /// <summary>Pojedyncze zapytanie do baz City + ASN; null = brak danych dla IP.</summary>
    private GeoInfo? Lookup(string ip)
    {
        CityResponse? city = null;
        AsnResponse? asn = null;

        try
        {
            _cityReader?.TryCity(ip, out city);
            _asnReader?.TryAsn(ip, out asn);
        }
        catch (Exception ex)
        {
            // Np. niepoprawny format IP — zdarzenie płynie dalej bez geo.
            _logger.LogDebug(ex, "Zapytanie GeoIP dla {Ip} nie powiodło się.", ip);
            return null;
        }

        if (city is null && asn is null)
        {
            return null;
        }

        return new GeoInfo
        {
            Country = city?.Country.IsoCode,
            CountryName = city?.Country.Name,
            City = city?.City.Name,
            Lat = city?.Location.Latitude,
            Lon = city?.Location.Longitude,
            Asn = asn?.AutonomousSystemNumber is { } number ? $"AS{number}" : null,
            Org = asn?.AutonomousSystemOrganization,
        };
    }

    /// <summary>Otwiera bazę .mmdb; brak pliku => null + jedno polskie ostrzeżenie.</summary>
    private DatabaseReader? TryOpenDatabase(string configuredPath, string label)
    {
        // Ścieżki względne rozwiązujemy względem katalogu binarki (w kontenerze: /app).
        var fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning(
                "Baza {Label} nie istnieje pod ścieżką {Path} — wzbogacanie GeoIP ({Label}) wyłączone. " +
                "Instrukcja pobrania: src/HoneyGrid.Ingestion/geoip/README.md.",
                label, fullPath, label);
            return null;
        }

        try
        {
            var reader = new DatabaseReader(fullPath);
            _logger.LogInformation("Załadowano bazę {Label} z {Path}.", label, fullPath);
            return reader;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się otworzyć bazy {Label} z {Path} — enricher wyłączony.", label, fullPath);
            return null;
        }
    }

    public void Dispose()
    {
        _cityReader?.Dispose();
        _asnReader?.Dispose();
    }
}
