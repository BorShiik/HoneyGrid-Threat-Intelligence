using System.Text.Json;
using HoneyGrid.Stix;

namespace HoneyGrid.Stix.Tests;

/// <summary>Testy serializacji SDO/SRO: snake_case, format id, brak pól null.</summary>
public sealed class StixSerializationTests
{
    private static StixBundle BuildSampleBundle()
    {
        var counter = 0;
        Guid Next() => Guid.Parse($"00000000-0000-0000-0000-{++counter:D12}");
        var now = new DateTimeOffset(2026, 6, 13, 10, 0, 0, TimeSpan.Zero);
        var iocs = new[]
        {
            new StixIocInput { IocType = StixIocType.Ipv4, Value = "203.0.113.45", Score = 90 },
            new StixIocInput { IocType = StixIocType.CredentialPattern, Value = "admin" },
        };
        return StixExportEngine.Build(iocs, Next, now);
    }

    [Fact]
    public void Serialize_UsesSnakeCaseSpecVersionKey()
    {
        var json = StixSerializer.ToJson(BuildSampleBundle());
        Assert.Contains("\"spec_version\"", json);
    }

    [Fact]
    public void Serialize_IndicatorHasPatternTypeAndValidFromSnakeCase()
    {
        var json = StixSerializer.ToJson(BuildSampleBundle());
        Assert.Contains("\"pattern_type\"", json);
        Assert.Contains("\"valid_from\"", json);
    }

    [Fact]
    public void Serialize_AttackPatternHasExternalReferences()
    {
        var json = StixSerializer.ToJson(BuildSampleBundle());
        Assert.Contains("\"external_references\"", json);
        Assert.Contains("\"source_name\"", json);
        Assert.Contains("\"external_id\"", json);
    }

    [Fact]
    public void Serialize_RelationshipHasSourceAndTargetRefSnakeCase()
    {
        var json = StixSerializer.ToJson(BuildSampleBundle());
        Assert.Contains("\"source_ref\"", json);
        Assert.Contains("\"target_ref\"", json);
    }

    [Fact]
    public void Serialize_NoNullFieldsEmitted()
    {
        var json = StixSerializer.ToJson(BuildSampleBundle());
        Assert.DoesNotContain(":null", json.Replace(" ", string.Empty));
    }

    [Fact]
    public void IdentityId_HasTypePrefixGuidFormat()
    {
        var bundle = BuildSampleBundle();
        var identity = Assert.IsType<StixIdentity>(bundle.Objects[0]);
        Assert.StartsWith("identity--", identity.Id);
        var guidPart = identity.Id["identity--".Length..];
        Assert.True(Guid.TryParse(guidPart, out _), "Część po '--' musi być poprawnym GUID-em.");
    }

    [Fact]
    public void BundleId_HasBundlePrefix()
    {
        var bundle = BuildSampleBundle();
        Assert.StartsWith("bundle--", bundle.Id);
        Assert.Equal("bundle", bundle.Type);
    }

    [Fact]
    public void Serialize_DerivedObjectFieldsArePresent_NotJustBaseType()
    {
        // Weryfikuje konwerter polimorficzny: pole 'pattern' (tylko Indicator) musi się pojawić.
        var json = StixSerializer.ToJson(BuildSampleBundle());
        Assert.Contains("\"pattern\"", json);
        Assert.Contains("[ipv4-addr:value = '203.0.113.45']", json);
    }
}
