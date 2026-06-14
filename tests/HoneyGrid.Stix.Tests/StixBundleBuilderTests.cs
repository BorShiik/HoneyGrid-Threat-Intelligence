using System.Text.Json;
using HoneyGrid.Stix;

namespace HoneyGrid.Stix.Tests;

/// <summary>Testy budowy bundla i round-trip JSON.</summary>
public sealed class StixBundleBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 10, 0, 0, TimeSpan.Zero);

    private static Func<Guid> SequentialGuids()
    {
        var counter = 0;
        return () =>
        {
            counter++;
            return Guid.Parse($"00000000-0000-0000-0000-{counter:D12}");
        };
    }

    [Fact]
    public void Build_EmptyInput_ReturnsIdentityOnlyBundle()
    {
        var bundle = StixExportEngine.Build([], SequentialGuids(), Now);
        Assert.Single(bundle.Objects);
        Assert.IsType<StixIdentity>(bundle.Objects[0]);
    }

    [Fact]
    public void Build_IdentityIsAlwaysFirstObject()
    {
        var iocs = new[] { new StixIocInput { IocType = StixIocType.Ipv4, Value = "1.2.3.4" } };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        var identity = Assert.IsType<StixIdentity>(bundle.Objects[0]);
        Assert.Equal("HoneyGrid Threat Intelligence Platform", identity.Name);
        Assert.Equal("system", identity.IdentityClass);
    }

    [Fact]
    public void Build_Ipv4Ioc_ProducesIndicatorWithIpv4Pattern()
    {
        var iocs = new[] { new StixIocInput { IocType = StixIocType.Ipv4, Value = "203.0.113.45" } };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        var indicator = bundle.Objects.OfType<StixIndicator>().Single();
        Assert.Equal("[ipv4-addr:value = '203.0.113.45']", indicator.Pattern);
        Assert.Equal("stix", indicator.PatternType);
        Assert.Contains("malicious-activity", indicator.IndicatorTypes);
    }

    [Fact]
    public void Build_Sha256Ioc_ProducesFileHashPattern_StrippingPrefix()
    {
        const string raw = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var iocs = new[] { new StixIocInput { IocType = StixIocType.Sha256, Value = raw } };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        var indicator = bundle.Objects.OfType<StixIndicator>().Single();
        Assert.Equal(
            "[file:hashes.'SHA-256' = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855']",
            indicator.Pattern);
    }

    [Fact]
    public void Build_CredentialIoc_ProducesIndicatorAttackPatternAndRelationship()
    {
        var iocs = new[] { new StixIocInput { IocType = StixIocType.CredentialPattern, Value = "admin" } };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);

        var indicator = bundle.Objects.OfType<StixIndicator>().Single();
        var attackPattern = bundle.Objects.OfType<StixAttackPattern>().Single();
        var relationship = bundle.Objects.OfType<StixRelationship>().Single();

        Assert.Equal("[user-account:account_login = 'admin']", indicator.Pattern);
        var extRef = Assert.Single(attackPattern.ExternalReferences);
        Assert.Equal("mitre-attack", extRef.SourceName);
        Assert.Equal("T1110", extRef.ExternalId);
        Assert.Equal("indicates", relationship.RelationshipType);
        Assert.Equal(indicator.Id, relationship.SourceRef);
        Assert.Equal(attackPattern.Id, relationship.TargetRef);
    }

    [Fact]
    public void Build_RespectsCustomMitreTechnique()
    {
        var iocs = new[]
        {
            new StixIocInput { IocType = StixIocType.CredentialPattern, Value = "root", MitreTechnique = "T1078" },
        };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        var extRef = bundle.Objects.OfType<StixAttackPattern>().Single().ExternalReferences.Single();
        Assert.Equal("T1078", extRef.ExternalId);
    }

    [Fact]
    public void Build_FirstSeenDrivesValidFrom()
    {
        var firstSeen = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var iocs = new[] { new StixIocInput { IocType = StixIocType.Ipv4, Value = "9.9.9.9", FirstSeen = firstSeen } };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        var indicator = bundle.Objects.OfType<StixIndicator>().Single();
        Assert.Equal(firstSeen, indicator.ValidFrom);
    }

    [Fact]
    public void Build_SkipsBlankValues()
    {
        var iocs = new[]
        {
            new StixIocInput { IocType = StixIocType.Ipv4, Value = "   " },
            new StixIocInput { IocType = StixIocType.Ipv4, Value = "8.8.8.8" },
        };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        Assert.Single(bundle.Objects.OfType<StixIndicator>());
    }

    [Fact]
    public void RoundTrip_BundleSerializesToValidJsonStructure()
    {
        var iocs = new[]
        {
            new StixIocInput { IocType = StixIocType.Ipv4, Value = "203.0.113.45", Score = 88 },
            new StixIocInput { IocType = StixIocType.CredentialPattern, Value = "admin" },
        };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        var json = StixSerializer.ToJson(bundle);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("bundle", root.GetProperty("type").GetString());
        var objects = root.GetProperty("objects");
        Assert.Equal(JsonValueKind.Array, objects.ValueKind);
        // identity + indicator(ip) + indicator(cred) + attack-pattern + relationship = 5
        Assert.Equal(5, objects.GetArrayLength());

        foreach (var obj in objects.EnumerateArray())
        {
            Assert.True(obj.TryGetProperty("type", out _), "Każdy obiekt musi mieć 'type'.");
            Assert.True(obj.TryGetProperty("id", out var idProp), "Każdy obiekt musi mieć 'id'.");
            var id = idProp.GetString()!;
            var type = obj.GetProperty("type").GetString()!;
            Assert.StartsWith($"{type}--", id);
        }
    }

    [Fact]
    public void RoundTrip_FirstObjectIsIdentity()
    {
        var bundle = StixExportEngine.Build(
            [new StixIocInput { IocType = StixIocType.Ipv4, Value = "1.1.1.1" }],
            SequentialGuids(),
            Now);
        var json = StixSerializer.ToJson(bundle);
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("objects")[0];
        Assert.Equal("identity", first.GetProperty("type").GetString());
    }

    [Fact]
    public void Build_SharesAttackPatternAcrossCredentialIocsOfSameTechnique()
    {
        var iocs = new[]
        {
            new StixIocInput { IocType = StixIocType.CredentialPattern, Value = "admin" },
            new StixIocInput { IocType = StixIocType.CredentialPattern, Value = "root" },
        };
        var bundle = StixExportEngine.Build(iocs, SequentialGuids(), Now);
        // Jeden współdzielony Attack-Pattern, ale dwie relacje (po jednej na wskaźnik).
        Assert.Single(bundle.Objects.OfType<StixAttackPattern>());
        Assert.Equal(2, bundle.Objects.OfType<StixRelationship>().Count());
    }
}
