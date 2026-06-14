using HoneyGrid.Contracts;
using HoneyGrid.Functions.Profiling;

namespace HoneyGrid.Functions.Tests;

public class FingerprintBuilderTests
{
    [Fact]
    public void Groups_events_by_attacker_ip()
    {
        var events = new[]
        {
            TestEvents.Event("10.0.0.1", username: "root", password: "123456"),
            TestEvents.Event("10.0.0.1", EventType.Command, command: "uname -a"),
            TestEvents.Event("10.0.0.2", username: "admin", password: "admin"),
        };

        var fps = FingerprintBuilder.FromEvents(events);

        Assert.Equal(2, fps.Count);
        var first = fps.Single(f => f.AttackerIp == "10.0.0.1");
        Assert.Equal(2, first.EventCount);
        Assert.Contains("root:123456", first.Credentials);
        Assert.Contains("uname -a", first.Commands);
    }

    [Fact]
    public void Aggregates_asn_country_sensor_and_hours()
    {
        var ts = new DateTimeOffset(2026, 6, 1, 4, 30, 0, TimeSpan.Zero);
        var events = new[]
        {
            TestEvents.Event("10.0.0.1", asn: "AS4134", country: "CN", sensorType: SensorType.Ssh, timestamp: ts),
        };

        var fp = FingerprintBuilder.FromEvents(events).Single();

        Assert.Contains("AS4134", fp.Asns);
        Assert.Contains("CN", fp.Countries);
        Assert.Contains("ssh", fp.SensorTypes);
        Assert.True(fp.ActivityHours[4]);
        Assert.False(fp.ActivityHours[5]);
    }

    [Fact]
    public void Averages_sophistication_over_classified_events()
    {
        var events = new[]
        {
            TestEvents.Event("10.0.0.1", sophistication: 0.2),
            TestEvents.Event("10.0.0.1", sophistication: 0.8),
        };

        var fp = FingerprintBuilder.FromEvents(events).Single();

        Assert.Equal(0.5, fp.AvgSophistication, precision: 6);
    }

    [Fact]
    public void Output_is_sorted_by_ip_for_determinism()
    {
        var events = new[]
        {
            TestEvents.Event("10.0.0.9"),
            TestEvents.Event("10.0.0.1"),
            TestEvents.Event("10.0.0.5"),
        };

        var ips = FingerprintBuilder.FromEvents(events).Select(f => f.AttackerIp).ToArray();

        Assert.Equal(["10.0.0.1", "10.0.0.5", "10.0.0.9"], ips);
    }
}
