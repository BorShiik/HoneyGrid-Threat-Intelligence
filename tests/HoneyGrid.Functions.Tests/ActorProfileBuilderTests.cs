using HoneyGrid.Contracts;
using HoneyGrid.Functions.Profiling;

namespace HoneyGrid.Functions.Tests;

public class ActorProfileBuilderTests
{
    private static IReadOnlyList<ActorFingerprint> Cluster(params HoneypotEvent[] events) =>
        FingerprintBuilder.FromEvents(events);

    [Fact]
    public void Cryptominer_commands_yield_cryptominer_archetype_and_automated_intent()
    {
        var cluster = Cluster(
            TestEvents.Event("1.1.1.1", EventType.Command, command: "./xmrig -o pool.minexmr.com:4444", asn: "AS1", country: "RU",
                sophistication: 0.5, malicious: true));

        var actor = ActorProfileBuilder.Build(cluster);

        Assert.Equal("automated", actor.Intent);
        Assert.Contains("RU", actor.Countries);
        Assert.Single(actor.KnownIps);
        Assert.StartsWith("actor-", actor.Id);
    }

    [Theory]
    [InlineData(0.1, "minimal")]
    [InlineData(0.5, "intermediate")]
    [InlineData(0.9, "advanced")]
    public void Sophistication_buckets_map_correctly(double avg, string expected)
    {
        Assert.Equal(expected, ActorProfileBuilder.SophisticationBucket(avg));
    }

    [Fact]
    public void High_volume_malicious_advanced_actor_is_critical()
    {
        var severity = ActorProfileBuilder.Severity(avgSoph: 0.8, eventCount: 9000, malicious: true);
        Assert.Equal("critical", severity);
    }

    [Fact]
    public void Low_volume_unsophisticated_actor_is_low()
    {
        var severity = ActorProfileBuilder.Severity(avgSoph: 0.1, eventCount: 5, malicious: false);
        Assert.Equal("low", severity);
    }

    [Fact]
    public void Profile_aggregates_known_ips_sorted_and_event_count()
    {
        var cluster = Cluster(
            TestEvents.Event("2.2.2.2", username: "root", password: "123456", asn: "AS1", country: "CN"),
            TestEvents.Event("1.1.1.1", username: "root", password: "123456", asn: "AS1", country: "CN"));

        var actor = ActorProfileBuilder.Build(cluster);

        Assert.Equal(["1.1.1.1", "2.2.2.2"], actor.KnownIps);
        Assert.Equal(2, actor.EventCount);
        Assert.False(string.IsNullOrWhiteSpace(actor.Description));
    }
}
