using HoneyGrid.Contracts;
using HoneyGrid.Contracts.Validation;

namespace HoneyGrid.Contracts.Tests;

/// <summary>Testy walidatora FluentValidation dla HoneypotEvent.</summary>
public class ValidationTests
{
    private readonly HoneypotEventValidator _validator = new();

    private static HoneypotEvent CreateValidEvent() => new()
    {
        Id = Guid.NewGuid(),
        AttackerIp = "203.0.113.45",
        SensorId = "ssh-eu-01",
        Timestamp = DateTimeOffset.UtcNow,
        EventType = EventType.Connect,
    };

    [Fact]
    public void ValidEvent_PassesValidation()
    {
        var result = _validator.Validate(CreateValidEvent());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyId_FailsValidation()
    {
        var evt = CreateValidEvent() with { Id = Guid.Empty };
        var result = _validator.Validate(evt);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HoneypotEvent.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingAttackerIp_FailsValidation(string attackerIp)
    {
        var evt = CreateValidEvent() with { AttackerIp = attackerIp };
        var result = _validator.Validate(evt);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HoneypotEvent.AttackerIp));
    }

    [Fact]
    public void MissingSensorId_FailsValidation()
    {
        var evt = CreateValidEvent() with { SensorId = "" };
        var result = _validator.Validate(evt);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HoneypotEvent.SensorId));
    }

    [Fact]
    public void DefaultTimestamp_FailsValidation()
    {
        var evt = CreateValidEvent() with { Timestamp = default };
        var result = _validator.Validate(evt);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HoneypotEvent.Timestamp));
    }

    [Fact]
    public void UndefinedEventType_FailsValidation()
    {
        var evt = CreateValidEvent() with { EventType = (EventType)999 };
        var result = _validator.Validate(evt);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HoneypotEvent.EventType));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ThreatIntelScore_OutOfRange_FailsValidation(int score)
    {
        var evt = CreateValidEvent() with
        {
            ThreatIntel = new ThreatIntelInfo { Score = score },
        };
        var result = _validator.Validate(evt);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Sophistication_OutOfRange_FailsValidation(double sophistication)
    {
        var evt = CreateValidEvent() with
        {
            Classification = new ClassificationInfo { Sophistication = sophistication },
        };
        var result = _validator.Validate(evt);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void FullyEnrichedEvent_WithValidRanges_PassesValidation()
    {
        var evt = CreateValidEvent() with
        {
            ThreatIntel = new ThreatIntelInfo { KnownMalicious = true, Sources = ["AbuseIPDB"], Score = 87 },
            Classification = new ClassificationInfo { KillChainPhase = KillChainPhase.Delivery, Sophistication = 0.3 },
        };
        var result = _validator.Validate(evt);

        Assert.True(result.IsValid);
    }
}
