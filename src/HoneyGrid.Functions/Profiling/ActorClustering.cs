namespace HoneyGrid.Functions.Profiling;

/// <summary>
/// Zachłanna klasteryzacja odcisków (<see cref="ActorFingerprint"/>) w „aktorów".
///
/// Podobieństwo to ważona mieszanka: Jaccard po poleceniach+poświadczeniach
/// (zachowanie), nakładanie się ASN (infrastruktura) i bliskość okien czasowych
/// aktywności (timing). Dla czystych skanerów (brak poleceń/poświadczeń) waga
/// przenosi się na infrastrukturę + geografię + timing.
///
/// Algorytm zachłanny (jeden przebieg, próg scalania) — wystarczający dla
/// projektu kursowego i w pełni deterministyczny przy posortowanym wejściu.
/// Czysta logika, bez zależności od Azure.
/// </summary>
public static class ActorClustering
{
    /// <summary>Domyślny próg scalania (0–1). Wyższy = ostrożniejsze łączenie.</summary>
    public const double DefaultThreshold = 0.34;

    /// <summary>
    /// Grupuje odciski w klastry. Każdy klaster to lista odcisków przypisanych
    /// jednemu aktorowi. Wejście jest sortowane po IP dla determinizmu.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ActorFingerprint>> Cluster(
        IEnumerable<ActorFingerprint> fingerprints,
        double threshold = DefaultThreshold)
    {
        var sorted = fingerprints.OrderBy(f => f.AttackerIp, StringComparer.Ordinal).ToList();
        var clusters = new List<List<ActorFingerprint>>();
        var reps = new List<ActorFingerprint>(); // reprezentant (scalony odcisk) każdego klastra

        foreach (var fp in sorted)
        {
            var bestIndex = -1;
            var bestScore = threshold;

            for (var i = 0; i < reps.Count; i++)
            {
                var score = Similarity(reps[i], fp);
                if (score >= bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                clusters[bestIndex].Add(fp);
                reps[bestIndex] = Merge(reps[bestIndex], fp);
            }
            else
            {
                clusters.Add([fp]);
                reps.Add(fp);
            }
        }

        return clusters;
    }

    /// <summary>Podobieństwo dwóch odcisków w zakresie 0–1.</summary>
    public static double Similarity(ActorFingerprint a, ActorFingerprint b)
    {
        var behavior = Jaccard(Union(a.Commands, a.Credentials), Union(b.Commands, b.Credentials));
        var infra = Jaccard(a.Asns, b.Asns);
        var geo = Jaccard(a.Countries, b.Countries);
        var timing = HourOverlap(a.ActivityHours, b.ActivityHours);

        var hasBehavior = (a.Commands.Count + a.Credentials.Count) > 0
            && (b.Commands.Count + b.Credentials.Count) > 0;

        // Profil z zachowaniem (SSH post-exploitation) vs. czyste skanery.
        return hasBehavior
            ? 0.50 * behavior + 0.25 * infra + 0.10 * geo + 0.15 * timing
            : 0.55 * infra + 0.25 * geo + 0.20 * timing;
    }

    private static HashSet<string> Union(IReadOnlySet<string> x, IReadOnlySet<string> y)
    {
        var set = new HashSet<string>(x, StringComparer.Ordinal);
        set.UnionWith(y);
        return set;
    }

    private static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0;
        var intersection = a.Count <= b.Count
            ? a.Count(b.Contains)
            : b.Count(a.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static double HourOverlap(bool[] a, bool[] b)
    {
        int inter = 0, union = 0;
        for (var h = 0; h < 24; h++)
        {
            var x = a[h];
            var y = b[h];
            if (x && y) inter++;
            if (x || y) union++;
        }
        return union == 0 ? 0 : (double)inter / union;
    }

    private static ActorFingerprint Merge(ActorFingerprint a, ActorFingerprint b)
    {
        var hours = new bool[24];
        for (var h = 0; h < 24; h++) hours[h] = a.ActivityHours[h] || b.ActivityHours[h];

        var totalEvents = a.EventCount + b.EventCount;
        var avgSoph = totalEvents == 0
            ? 0
            : (a.AvgSophistication * a.EventCount + b.AvgSophistication * b.EventCount) / totalEvents;

        return new ActorFingerprint
        {
            // Reprezentant nie reprezentuje pojedynczego IP — etykieta poglądowa.
            AttackerIp = string.CompareOrdinal(a.AttackerIp, b.AttackerIp) <= 0 ? a.AttackerIp : b.AttackerIp,
            Commands = Union(a.Commands, b.Commands),
            Credentials = Union(a.Credentials, b.Credentials),
            Asns = Union(a.Asns, b.Asns),
            Countries = Union(a.Countries, b.Countries),
            SensorTypes = Union(a.SensorTypes, b.SensorTypes),
            ActivityHours = hours,
            EventCount = totalEvents,
            FirstSeen = a.FirstSeen < b.FirstSeen ? a.FirstSeen : b.FirstSeen,
            LastSeen = a.LastSeen > b.LastSeen ? a.LastSeen : b.LastSeen,
            AvgSophistication = avgSoph,
            KnownMalicious = a.KnownMalicious || b.KnownMalicious,
        };
    }
}
