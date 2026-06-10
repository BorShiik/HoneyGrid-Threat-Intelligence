using System.Text.Json;
using HoneyGrid.Contracts;

namespace HoneyGrid.Contracts.Tests;

/// <summary>
/// Testy mapowania enumów na wartości tekstowe JSON
/// (w tym wartości z kropkami, np. "login.failed").
/// </summary>
public class EnumMappingTests
{
    [Theory]
    [InlineData(SensorType.Ssh, "ssh")]
    [InlineData(SensorType.Web, "web")]
    [InlineData(SensorType.Rdp, "rdp")]
    public void SensorType_SerializesTo_LowercaseString(SensorType value, string expected)
    {
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, HoneyGridJson.Options));
    }

    [Theory]
    [InlineData("ssh", SensorType.Ssh)]
    [InlineData("web", SensorType.Web)]
    [InlineData("rdp", SensorType.Rdp)]
    public void SensorType_DeserializesFrom_LowercaseString(string json, SensorType expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<SensorType>($"\"{json}\"", HoneyGridJson.Options));
    }

    [Theory]
    [InlineData(EventType.LoginFailed, "login.failed")]
    [InlineData(EventType.LoginSuccess, "login.success")]
    [InlineData(EventType.Command, "command")]
    [InlineData(EventType.HttpRequest, "http.request")]
    [InlineData(EventType.Connect, "connect")]
    public void EventType_SerializesTo_DottedString(EventType value, string expected)
    {
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, HoneyGridJson.Options));
    }

    [Theory]
    [InlineData("login.failed", EventType.LoginFailed)]
    [InlineData("login.success", EventType.LoginSuccess)]
    [InlineData("command", EventType.Command)]
    [InlineData("http.request", EventType.HttpRequest)]
    [InlineData("connect", EventType.Connect)]
    public void EventType_DeserializesFrom_DottedString(string json, EventType expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<EventType>($"\"{json}\"", HoneyGridJson.Options));
    }

    [Theory]
    [InlineData(KillChainPhase.Recon, "recon")]
    [InlineData(KillChainPhase.Weaponization, "weaponization")]
    [InlineData(KillChainPhase.Delivery, "delivery")]
    [InlineData(KillChainPhase.Exploitation, "exploitation")]
    [InlineData(KillChainPhase.Installation, "installation")]
    [InlineData(KillChainPhase.C2, "c2")]
    [InlineData(KillChainPhase.Actions, "actions")]
    public void KillChainPhase_RoundTrips_AsLowercaseString(KillChainPhase value, string expected)
    {
        var json = JsonSerializer.Serialize(value, HoneyGridJson.Options);
        Assert.Equal($"\"{expected}\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<KillChainPhase>(json, HoneyGridJson.Options));
    }

    [Fact]
    public void EnumConstants_MatchStringConstantClasses()
    {
        // Stałe tekstowe (do KQL) muszą być spójne z mapowaniem enumów JSON.
        Assert.Equal(SensorTypes.Ssh, JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(SensorType.Ssh, HoneyGridJson.Options)));
        Assert.Equal(EventTypes.LoginFailed, JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(EventType.LoginFailed, HoneyGridJson.Options)));
        Assert.Equal(EventTypes.HttpRequest, JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(EventType.HttpRequest, HoneyGridJson.Options)));
        Assert.Equal(5, EventTypes.All.Count);
        Assert.Equal(3, SensorTypes.All.Count);
    }
}
