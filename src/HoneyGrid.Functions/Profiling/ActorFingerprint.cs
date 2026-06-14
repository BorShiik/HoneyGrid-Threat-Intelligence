using HoneyGrid.Contracts;

namespace HoneyGrid.Functions.Profiling;

/// <summary>
/// Odcisk behawioralny pojedynczego źródła (IP) — zagregowany z jego zdarzeń.
/// Stanowi podstawę korelacji aktywności w „aktorów" (klastry). Czysta struktura
/// danych, bez zależności od Azure → łatwa do testów jednostkowych.
/// </summary>
public sealed record ActorFingerprint
{
    public required string AttackerIp { get; init; }

    /// <summary>Znormalizowane polecenia (po uzyskaniu dostępu).</summary>
    public required IReadOnlySet<string> Commands { get; init; }

    /// <summary>Pary poświadczeń w formacie "login:hasło".</summary>
    public required IReadOnlySet<string> Credentials { get; init; }

    /// <summary>Numery ASN powiązane z infrastrukturą.</summary>
    public required IReadOnlySet<string> Asns { get; init; }

    /// <summary>Kody krajów (ISO 3166-1 alpha-2).</summary>
    public required IReadOnlySet<string> Countries { get; init; }

    /// <summary>Typy sensorów, które zarejestrowały aktywność (ssh|web|rdp).</summary>
    public required IReadOnlySet<string> SensorTypes { get; init; }

    /// <summary>Profil aktywności godzinowej (UTC) — 24 wartości true/false.</summary>
    public required bool[] ActivityHours { get; init; }

    public required int EventCount { get; init; }
    public required DateTimeOffset FirstSeen { get; init; }
    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>Średnie zaawansowanie (0–1) z klasyfikacji zdarzeń.</summary>
    public required double AvgSophistication { get; init; }

    /// <summary>Czy którekolwiek źródło było oznaczone jako złośliwe (TI).</summary>
    public required bool KnownMalicious { get; init; }
}

/// <summary>Buduje odciski (<see cref="ActorFingerprint"/>) ze strumienia zdarzeń.</summary>
public static class FingerprintBuilder
{
    private const int CommandMaxLength = 80;

    /// <summary>
    /// Grupuje zdarzenia po <c>attackerIp</c> i wylicza odcisk dla każdego źródła.
    /// Deterministyczne (wynik posortowany po IP) — istotne dla powtarzalności
    /// klasteryzacji i testów.
    /// </summary>
    public static IReadOnlyList<ActorFingerprint> FromEvents(IEnumerable<HoneypotEvent> events)
    {
        var byIp = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (var evt in events)
        {
            if (string.IsNullOrWhiteSpace(evt.AttackerIp)) continue;
            if (!byIp.TryGetValue(evt.AttackerIp, out var acc))
            {
                acc = new Accumulator(evt.AttackerIp);
                byIp[evt.AttackerIp] = acc;
            }
            acc.Add(evt);
        }

        return byIp.Values
            .OrderBy(a => a.AttackerIp, StringComparer.Ordinal)
            .Select(a => a.Build())
            .ToList();
    }

    /// <summary>Normalizuje polecenie do porównań: małe litery, przycięcie długości.</summary>
    public static string NormalizeCommand(string command)
    {
        var trimmed = command.Trim().ToLowerInvariant();
        return trimmed.Length > CommandMaxLength ? trimmed[..CommandMaxLength] : trimmed;
    }

    private sealed class Accumulator(string attackerIp)
    {
        public string AttackerIp { get; } = attackerIp;

        private readonly HashSet<string> _commands = new(StringComparer.Ordinal);
        private readonly HashSet<string> _credentials = new(StringComparer.Ordinal);
        private readonly HashSet<string> _asns = new(StringComparer.Ordinal);
        private readonly HashSet<string> _countries = new(StringComparer.Ordinal);
        private readonly HashSet<string> _sensorTypes = new(StringComparer.Ordinal);
        private readonly bool[] _hours = new bool[24];

        private int _count;
        private double _sophisticationSum;
        private int _sophisticationCount;
        private bool _malicious;
        private DateTimeOffset _first = DateTimeOffset.MaxValue;
        private DateTimeOffset _last = DateTimeOffset.MinValue;

        public void Add(HoneypotEvent evt)
        {
            _count++;

            if (evt.Timestamp < _first) _first = evt.Timestamp;
            if (evt.Timestamp > _last) _last = evt.Timestamp;
            _hours[evt.Timestamp.UtcDateTime.Hour] = true;

            if (!string.IsNullOrWhiteSpace(evt.Command))
            {
                _commands.Add(NormalizeCommand(evt.Command));
            }

            if (evt.Credentials is { Username: { } u } && !string.IsNullOrEmpty(u))
            {
                _credentials.Add($"{u}:{evt.Credentials.Password ?? ""}");
            }

            if (!string.IsNullOrWhiteSpace(evt.Geo?.Asn)) _asns.Add(evt.Geo!.Asn!);
            if (!string.IsNullOrWhiteSpace(evt.Geo?.Country)) _countries.Add(evt.Geo!.Country!);
            if (evt.SensorType is { } st) _sensorTypes.Add(st.ToString().ToLowerInvariant());

            if (evt.Classification?.Sophistication is { } soph)
            {
                _sophisticationSum += soph;
                _sophisticationCount++;
            }

            if (evt.ThreatIntel?.KnownMalicious == true) _malicious = true;
        }

        public ActorFingerprint Build() => new()
        {
            AttackerIp = AttackerIp,
            Commands = _commands,
            Credentials = _credentials,
            Asns = _asns,
            Countries = _countries,
            SensorTypes = _sensorTypes,
            ActivityHours = _hours,
            EventCount = _count,
            FirstSeen = _first == DateTimeOffset.MaxValue ? _last : _first,
            LastSeen = _last,
            AvgSophistication = _sophisticationCount > 0 ? _sophisticationSum / _sophisticationCount : 0,
            KnownMalicious = _malicious,
        };
    }
}
