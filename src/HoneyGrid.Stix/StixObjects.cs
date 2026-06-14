using System.Text.Json.Serialization;

namespace HoneyGrid.Stix;

/// <summary>
/// Wspólna baza wszystkich obiektów STIX (SDO/SRO/Bundle).
/// Modelowana jako klasa abstrakcyjna, aby heterogeniczna tablica
/// <c>objects</c> w Bundle serializowała każdy obiekt z jego własnymi polami.
/// Wszystkie pola: snake_case, null pomijane (zob. <see cref="StixJson.Options"/>).
/// </summary>
public abstract record StixObject
{
    /// <summary>Typ obiektu STIX, np. 'identity', 'indicator', 'bundle'.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Identyfikator w formacie '&lt;type&gt;--&lt;uuid&gt;'.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

/// <summary>
/// Wspólna baza dla obiektów wersjonowanych STIX (SDO/SRO) — posiadają
/// <c>spec_version</c> oraz znaczniki czasu <c>created</c>/<c>modified</c>.
/// </summary>
public abstract record StixVersionedObject : StixObject
{
    /// <summary>Wersja specyfikacji STIX (wymagane: '2.1').</summary>
    [JsonPropertyName("spec_version")]
    public string SpecVersion { get; init; } = "2.1";

    /// <summary>Znacznik czasu utworzenia obiektu (UTC).</summary>
    [JsonPropertyName("created")]
    public required DateTimeOffset Created { get; init; }

    /// <summary>Znacznik czasu ostatniej modyfikacji obiektu (UTC).</summary>
    [JsonPropertyName("modified")]
    public required DateTimeOffset Modified { get; init; }
}

/// <summary>
/// SDO Identity — tożsamość platformy publikującej dane threat intelligence.
/// </summary>
public sealed record StixIdentity : StixVersionedObject
{
    /// <summary>Nazwa wyświetlana tożsamości.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Klasa tożsamości STIX, np. 'system', 'organization'.</summary>
    [JsonPropertyName("identity_class")]
    public string IdentityClass { get; init; } = "system";
}

/// <summary>
/// SDO Indicator — wykrywalny wzorzec (pattern STIX) sygnalizujący aktywność złośliwą.
/// </summary>
public sealed record StixIndicator : StixVersionedObject
{
    /// <summary>Nazwa wskaźnika.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Opis wskaźnika.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Język wzorca (wymagane: 'stix').</summary>
    [JsonPropertyName("pattern_type")]
    public string PatternType { get; init; } = "stix";

    /// <summary>Wzorzec STIX, np. "[ipv4-addr:value = '203.0.113.45']".</summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    /// <summary>Kategorie wskaźnika, np. ["malicious-activity"].</summary>
    [JsonPropertyName("indicator_types")]
    public IReadOnlyList<string> IndicatorTypes { get; init; } = ["malicious-activity"];

    /// <summary>Moment, od którego wskaźnik jest ważny (UTC).</summary>
    [JsonPropertyName("valid_from")]
    public required DateTimeOffset ValidFrom { get; init; }
}

/// <summary>
/// SDO Attack-Pattern — technika ataku (mapowanie do MITRE ATT&amp;CK).
/// </summary>
public sealed record StixAttackPattern : StixVersionedObject
{
    /// <summary>Nazwa techniki, np. "Brute Force".</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Opis techniki.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Referencje zewnętrzne (m.in. mapowanie MITRE ATT&amp;CK).</summary>
    [JsonPropertyName("external_references")]
    public IReadOnlyList<StixExternalReference> ExternalReferences { get; init; } = [];
}

/// <summary>
/// SRO Relationship — relacja między dwoma obiektami STIX, np. indicator → attack-pattern.
/// </summary>
public sealed record StixRelationship : StixVersionedObject
{
    /// <summary>Typ relacji, np. 'indicates', 'uses'.</summary>
    [JsonPropertyName("relationship_type")]
    public required string RelationshipType { get; init; }

    /// <summary>Identyfikator obiektu źródłowego relacji.</summary>
    [JsonPropertyName("source_ref")]
    public required string SourceRef { get; init; }

    /// <summary>Identyfikator obiektu docelowego relacji.</summary>
    [JsonPropertyName("target_ref")]
    public required string TargetRef { get; init; }
}

/// <summary>
/// Referencja zewnętrzna STIX — używana m.in. do mapowania na MITRE ATT&amp;CK.
/// </summary>
public sealed record StixExternalReference
{
    /// <summary>Źródło referencji, np. 'mitre-attack'.</summary>
    [JsonPropertyName("source_name")]
    public required string SourceName { get; init; }

    /// <summary>Identyfikator zewnętrzny, np. 'T1110'.</summary>
    [JsonPropertyName("external_id")]
    public string? ExternalId { get; init; }

    /// <summary>Adres URL referencji.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>
/// STIX Bundle — kontener grupujący heterogeniczne obiekty STIX do publikacji.
/// </summary>
public sealed record StixBundle : StixObject
{
    /// <summary>Obiekty zawarte w bundlu (Identity, Indicator, Attack-Pattern, Relationship...).</summary>
    [JsonPropertyName("objects")]
    public required IReadOnlyList<StixObject> Objects { get; init; }
}
