namespace HoneyGrid.Functions.Profiling;

/// <summary>
/// Składa profil aktora (<see cref="ActorDocument"/>) z klastra odcisków.
///
/// Generuje też heurystyczne „dossier" (archetyp, intencja, poziom zagrożenia,
/// opis) bez modelu AI. To jednocześnie wersja zapasowa dla docelowego generatora
/// dossier opartego na Azure OpenAI (Tydzień 6): gdy model jest niedostępny lub
/// zwróci błąd, używamy tej deterministycznej heurystyki.
///
/// Czysta logika → testowalna.
/// </summary>
public static class ActorProfileBuilder
{
    public static ActorDocument Build(IReadOnlyList<ActorFingerprint> cluster)
    {
        ArgumentOutOfRangeException.ThrowIfZero(cluster.Count);

        var knownIps = cluster.Select(f => f.AttackerIp)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ip => ip, StringComparer.Ordinal)
            .ToList();

        var commands = Union(cluster, f => f.Commands);
        var credentials = Union(cluster, f => f.Credentials);
        var asns = Union(cluster, f => f.Asns);
        var countries = Union(cluster, f => f.Countries);
        var sensorTypes = Union(cluster, f => f.SensorTypes);

        var eventCount = cluster.Sum(f => (long)f.EventCount);
        var firstSeen = cluster.Min(f => f.FirstSeen);
        var lastSeen = cluster.Max(f => f.LastSeen);
        var avgSoph = eventCount == 0
            ? 0
            : cluster.Sum(f => f.AvgSophistication * f.EventCount) / eventCount;
        var malicious = cluster.Any(f => f.KnownMalicious);

        var seed = string.Join('|', knownIps);
        var sophistication = SophisticationBucket(avgSoph);
        var archetype = Archetype(commands, sensorTypes, sophistication);
        var intent = Intent(archetype, sophistication, knownIps.Count);
        var severity = Severity(avgSoph, eventCount, malicious);

        return new ActorDocument
        {
            Id = ActorNameGenerator.GenerateId(seed),
            Name = ActorNameGenerator.Generate(seed),
            FirstSeen = firstSeen,
            LastSeen = lastSeen,
            EventCount = eventCount,
            KnownIps = knownIps,
            Countries = countries.OrderBy(c => c, StringComparer.Ordinal).ToList(),
            Asns = asns.OrderBy(a => a, StringComparer.Ordinal).ToList(),
            Sophistication = sophistication,
            Intent = intent,
            Severity = severity,
            Description = Describe(archetype, countries, knownIps.Count, eventCount, commands),
        };
    }

    public static string SophisticationBucket(double avg) =>
        avg < 0.34 ? "minimal" : avg < 0.67 ? "intermediate" : "advanced";

    public static string Archetype(
        IReadOnlySet<string> commands, IReadOnlySet<string> sensorTypes, string sophistication)
    {
        if (ContainsAny(commands, "xmrig", "minexmr", "miner", "stratum")) return "cryptominer";
        if (ContainsAny(commands, "wget", "curl", "mirai", "tftp", "busybox", "/bins/")) return "botnet-operator";
        if (commands.Count == 0 && sensorTypes.Contains("web")) return "web-scanner";
        if (commands.Count == 0) return "scanner";
        return sophistication == "advanced" ? "APT-like" : "script-kiddie";
    }

    public static string Intent(string archetype, string sophistication, int ipCount) => archetype switch
    {
        "cryptominer" or "botnet-operator" => "automated",
        "APT-like" => "targeted",
        _ => sophistication == "advanced" && ipCount <= 2 ? "targeted" : "opportunistic",
    };

    public static string Severity(double avgSoph, long eventCount, bool malicious)
    {
        if (malicious && (avgSoph >= 0.67 || eventCount >= 5000)) return "critical";
        if (avgSoph >= 0.67 || eventCount >= 2000) return "high";
        if (avgSoph >= 0.34 || eventCount >= 200) return "medium";
        return "low";
    }

    private static string Describe(
        string archetype, IReadOnlySet<string> countries, int ipCount, long eventCount,
        IReadOnlySet<string> commands)
    {
        var origin = countries.Count > 0
            ? string.Join(", ", countries.OrderBy(c => c, StringComparer.Ordinal))
            : "nieznane lokalizacje";

        var behaviour = archetype switch
        {
            "cryptominer" => "po uzyskaniu dostępu uruchamia koparkę kryptowalut i utrwala obecność",
            "botnet-operator" => "pobiera i uruchamia binaria botnetu (wzorzec Mirai/loader)",
            "web-scanner" => "masowo skanuje ścieżki aplikacji webowych i panele administracyjne",
            "scanner" => "prowadzi rozpoznanie usług bez prób eksploitacji",
            "APT-like" => "działa metodycznie, z ograniczonej infrastruktury i z ręcznym rozpoznaniem",
            _ => "prowadzi automatyczne ataki słownikowe na usługi SSH/RDP",
        };

        var tooling = commands.Count > 0 ? $" Zaobserwowano {commands.Count} unikalnych poleceń." : "";

        return $"Aktor operuje z {ipCount} adresu/-ów IP ({origin}); {behaviour}. " +
               $"Łącznie {eventCount} zdarzeń.{tooling}";
    }

    private static HashSet<string> Union(
        IReadOnlyList<ActorFingerprint> cluster, Func<ActorFingerprint, IReadOnlySet<string>> selector)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fp in cluster) set.UnionWith(selector(fp));
        return set;
    }

    private static bool ContainsAny(IReadOnlySet<string> commands, params string[] needles)
    {
        foreach (var cmd in commands)
        {
            foreach (var needle in needles)
            {
                if (cmd.Contains(needle, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }
}
