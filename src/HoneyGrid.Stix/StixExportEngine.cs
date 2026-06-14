namespace HoneyGrid.Stix;

/// <summary>Rodzaj wskaźnika kompromitacji (IoC) na wejściu silnika eksportu.</summary>
public enum StixIocType
{
    /// <summary>Adres IPv4 atakującego.</summary>
    Ipv4,

    /// <summary>Hash SHA-256 pobranego artefaktu.</summary>
    Sha256,

    /// <summary>Wzorzec poświadczeń / brute-force (login).</summary>
    CredentialPattern,
}

/// <summary>
/// Lekki DTO wejściowy silnika eksportu STIX — neutralny wobec źródła danych
/// (Cosmos, testy, pliki). Mapowany na obiekty STIX przez <see cref="StixExportEngine"/>.
/// </summary>
public sealed record StixIocInput
{
    /// <summary>Rodzaj wskaźnika.</summary>
    public required StixIocType IocType { get; init; }

    /// <summary>Wartość wskaźnika (IP, hash, login).</summary>
    public required string Value { get; init; }

    /// <summary>Opcjonalny wynik reputacji 0–100.</summary>
    public int? Score { get; init; }

    /// <summary>Opcjonalny moment pierwszej obserwacji (UTC).</summary>
    public DateTimeOffset? FirstSeen { get; init; }

    /// <summary>Opcjonalny identyfikator techniki MITRE ATT&amp;CK, np. "T1110".</summary>
    public string? MitreTechnique { get; init; }
}

/// <summary>
/// Silnik eksportu STIX 2.1 — buduje <see cref="StixBundle"/> z listy wskaźników
/// (<see cref="StixIocInput"/>). Czysty .NET, bez zależności od Azure.
///
/// Każdy IoC → Indicator. Dla wzorców poświadczeń/brute-force dodatkowo
/// Attack-Pattern (domyślnie MITRE T1110) oraz Relationship 'indicates'
/// łączący Indicator z Attack-Pattern. Identity platformy jest zawsze objects[0].
///
/// Determinizm: GUID-y i znaczniki czasu można wstrzyknąć (testy stabilne).
/// </summary>
public static class StixExportEngine
{
    /// <summary>Wersja specyfikacji STIX wspierana przez silnik.</summary>
    public const string StixVersion = "2.1";

    /// <summary>Nazwa tożsamości platformy publikującej dane.</summary>
    public const string PlatformIdentityName = "HoneyGrid Threat Intelligence Platform";

    private const string MitreSourceName = "mitre-attack";
    private const string DefaultBruteForceTechnique = "T1110";

    /// <summary>
    /// Buduje bundle STIX z listy wskaźników. Puste wejście → bundle z samą Identity
    /// (nadal poprawny wg specyfikacji).
    /// </summary>
    /// <param name="iocs">Wskaźniki do wyeksportowania.</param>
    /// <param name="idFactory">Fabryka GUID-ów (wstrzykiwalna dla determinizmu w testach).</param>
    /// <param name="now">Znacznik czasu created/modified/valid_from (wstrzykiwalny).</param>
    public static StixBundle Build(
        IEnumerable<StixIocInput> iocs,
        Func<Guid>? idFactory = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(iocs);

        var newGuid = idFactory ?? Guid.NewGuid;
        var timestamp = now ?? DateTimeOffset.UtcNow;

        var objects = new List<StixObject>();

        // Identity platformy zawsze jako pierwszy obiekt bundla.
        var identity = new StixIdentity
        {
            Type = "identity",
            Id = $"identity--{newGuid()}",
            Created = timestamp,
            Modified = timestamp,
            Name = PlatformIdentityName,
            IdentityClass = "system",
        };
        objects.Add(identity);

        // Cache Attack-Patternów per technika — jedna technika → jeden SDO współdzielony.
        var attackPatterns = new Dictionary<string, StixAttackPattern>(StringComparer.Ordinal);

        foreach (var ioc in iocs)
        {
            if (ioc is null || string.IsNullOrWhiteSpace(ioc.Value))
            {
                continue;
            }

            var (pattern, name, indicatorType) = MapIoc(ioc);

            var indicator = new StixIndicator
            {
                Type = "indicator",
                Id = $"indicator--{newGuid()}",
                Created = timestamp,
                Modified = timestamp,
                Name = name,
                Description = BuildDescription(ioc),
                Pattern = pattern,
                PatternType = "stix",
                IndicatorTypes = [indicatorType],
                ValidFrom = ioc.FirstSeen ?? timestamp,
            };
            objects.Add(indicator);

            // Wzorzec poświadczeń → Attack-Pattern (MITRE) + Relationship 'indicates'.
            if (ioc.IocType == StixIocType.CredentialPattern)
            {
                var techniqueId = string.IsNullOrWhiteSpace(ioc.MitreTechnique)
                    ? DefaultBruteForceTechnique
                    : ioc.MitreTechnique!;

                if (!attackPatterns.TryGetValue(techniqueId, out var attackPattern))
                {
                    attackPattern = new StixAttackPattern
                    {
                        Type = "attack-pattern",
                        Id = $"attack-pattern--{newGuid()}",
                        Created = timestamp,
                        Modified = timestamp,
                        Name = TechniqueName(techniqueId),
                        ExternalReferences =
                        [
                            new StixExternalReference
                            {
                                SourceName = MitreSourceName,
                                ExternalId = techniqueId,
                                Url = $"https://attack.mitre.org/techniques/{techniqueId}/",
                            },
                        ],
                    };
                    attackPatterns[techniqueId] = attackPattern;
                    objects.Add(attackPattern);
                }

                objects.Add(new StixRelationship
                {
                    Type = "relationship",
                    Id = $"relationship--{newGuid()}",
                    Created = timestamp,
                    Modified = timestamp,
                    RelationshipType = "indicates",
                    SourceRef = indicator.Id,
                    TargetRef = attackPattern.Id,
                });
            }
        }

        return new StixBundle
        {
            Type = "bundle",
            Id = $"bundle--{newGuid()}",
            Objects = objects,
        };
    }

    /// <summary>Zachowane dla zgodności wstecznej: prosty wzorzec IPv4 z pojedynczego zdarzenia.</summary>
    public static string MapToIndicatorPattern(HoneyGrid.Contracts.HoneypotEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return StixPattern.Ipv4(evt.AttackerIp);
    }

    private static (string Pattern, string Name, string IndicatorType) MapIoc(StixIocInput ioc) =>
        ioc.IocType switch
        {
            StixIocType.Ipv4 => (
                StixPattern.Ipv4(ioc.Value),
                $"Malicious IP {ioc.Value}",
                "malicious-activity"),
            StixIocType.Sha256 => (
                StixPattern.FileSha256(NormalizeHash(ioc.Value)),
                $"Malicious file {NormalizeHash(ioc.Value)}",
                "malicious-activity"),
            StixIocType.CredentialPattern => (
                StixPattern.UserAccount(ioc.Value),
                $"Brute-force login '{ioc.Value}'",
                "attribution"),
            _ => throw new ArgumentOutOfRangeException(nameof(ioc), ioc.IocType, "Nieznany typ IoC."),
        };

    private static string BuildDescription(StixIocInput ioc)
    {
        var scorePart = ioc.Score is int s ? $" (reputation score {s})" : string.Empty;
        return ioc.IocType switch
        {
            StixIocType.Ipv4 => $"HoneyGrid observed malicious activity from {ioc.Value}{scorePart}.",
            StixIocType.Sha256 => $"HoneyGrid captured a malicious artifact with hash {NormalizeHash(ioc.Value)}{scorePart}.",
            StixIocType.CredentialPattern => $"HoneyGrid observed brute-force authentication using login '{ioc.Value}'{scorePart}.",
            _ => $"HoneyGrid indicator{scorePart}.",
        };
    }

    /// <summary>
    /// Usuwa prefiks algorytmu z hasha (kontrakt zapisuje "sha256:..."), zostawiając
    /// surową wartość dla wzorca STIX file:hashes.'SHA-256'.
    /// </summary>
    private static string NormalizeHash(string hash)
    {
        var idx = hash.IndexOf(':', StringComparison.Ordinal);
        return idx >= 0 ? hash[(idx + 1)..] : hash;
    }

    private static string TechniqueName(string techniqueId) => techniqueId switch
    {
        "T1110" => "Brute Force",
        _ => techniqueId,
    };
}
