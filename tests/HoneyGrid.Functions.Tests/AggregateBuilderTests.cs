using HoneyGrid.Contracts;
using HoneyGrid.Functions.Aggregation;

namespace HoneyGrid.Functions.Tests;

public class AggregateBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static HoneypotEvent[] Sample() =>
    [
        // A: dwa zdarzenia (login + komenda), CN, w ciągu 24 h
        TestEvents.Event("1.1.1.1", EventType.LoginFailed, username: "root", password: "123456",
            country: "CN", countryName: "Chiny", lat: 31.23, lon: 121.47, sensorType: SensorType.Ssh,
            timestamp: Now.AddHours(-1)),
        TestEvents.Event("1.1.1.1", EventType.Command, command: "uname -a", sensorType: SensorType.Ssh,
            timestamp: Now.AddHours(-2)),
        // B: poza oknem 24 h (ale w 7 dni), CN
        TestEvents.Event("2.2.2.2", EventType.LoginFailed, username: "root", password: "123456",
            country: "CN", countryName: "Chiny", lat: 31.23, lon: 121.47, sensorType: SensorType.Ssh,
            timestamp: Now.AddHours(-30)),
        // C: dwa skany web US; sess-1 aktywna (<5 min), sess-2 nie
        TestEvents.Event("3.3.3.3", EventType.HttpRequest, country: "US", countryName: "USA",
            lat: 39.0, lon: -77.5, sensorType: SensorType.Web, sessionId: "sess-1", timestamp: Now.AddMinutes(-2)),
        TestEvents.Event("3.3.3.3", EventType.HttpRequest, country: "US", countryName: "USA",
            lat: 39.0, lon: -77.5, sensorType: SensorType.Web, sessionId: "sess-2", timestamp: Now.AddMinutes(-10)),
    ];

    [Fact]
    public void Overview_counts_totals_uniques_and_active_sessions()
    {
        var o = AggregateBuilder.BuildOverview(Sample(), Now);

        Assert.Equal(5, o.TotalEvents);
        Assert.Equal(4, o.EventsLast24h); // wszystko poza zdarzeniem B (-30 h)
        Assert.Equal(3, o.UniqueAttackers); // A, B, C
        Assert.Equal(1, o.ActiveSessions); // tylko sess-1 (<5 min)
    }

    [Fact]
    public void Overview_breaks_down_by_country_sensor_and_type()
    {
        var o = AggregateBuilder.BuildOverview(Sample(), Now);

        Assert.Equal(2, o.TopCountries.Single(c => c.Country == "CN").Count);
        Assert.Equal(2, o.TopCountries.Single(c => c.Country == "US").Count);
        Assert.Equal("Chiny", o.TopCountries.Single(c => c.Country == "CN").CountryName);

        Assert.Equal(3, o.EventsBySensorType["ssh"]);
        Assert.Equal(2, o.EventsBySensorType["web"]);

        Assert.Equal(2, o.EventsByType["login.failed"]);
        Assert.Equal(1, o.EventsByType["command"]);
        Assert.Equal(2, o.EventsByType["http.request"]);
    }

    [Fact]
    public void Geo_aggregates_points_per_country_with_averaged_coordinates()
    {
        var g = AggregateBuilder.BuildGeo(Sample(), Now);

        Assert.Equal(2, g.Points.Count); // CN, US
        var cn = g.Points.Single(p => p.Country == "CN");
        Assert.Equal(2, cn.Count);
        Assert.Equal(31.23, cn.Lat, precision: 6);
        Assert.Equal(121.47, cn.Lon, precision: 6);
    }

    [Fact]
    public void Credentials_rank_usernames_passwords_pairs_and_total()
    {
        var c = AggregateBuilder.BuildCredentials(Sample(), Now);

        Assert.Equal(2, c.TotalAttempts); // dwa zdarzenia z poświadczeniami (A, B)
        Assert.Equal("root", c.TopUsernames[0].Username);
        Assert.Equal(2, c.TopUsernames[0].Count);
        Assert.Equal("123456", c.TopPasswords[0].Password);
        Assert.Equal(2, c.TopPasswords[0].Count);

        var pair = c.TopPairs[0];
        Assert.Equal("root", pair.Username);
        Assert.Equal("123456", pair.Password);
        Assert.Equal(2, pair.Count);
    }

    [Fact]
    public void Empty_input_produces_empty_aggregates()
    {
        var o = AggregateBuilder.BuildOverview([], Now);
        Assert.Equal(0, o.TotalEvents);
        Assert.Empty(o.TopCountries);

        var g = AggregateBuilder.BuildGeo([], Now);
        Assert.Empty(g.Points);

        var c = AggregateBuilder.BuildCredentials([], Now);
        Assert.Equal(0, c.TotalAttempts);
    }
}
