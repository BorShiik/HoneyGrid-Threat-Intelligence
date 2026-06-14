using HoneyGrid.Contracts;
using HoneyGrid.Functions.Classification;

namespace HoneyGrid.Functions.Tests;

public class StubClassifierTests
{
    [Theory]
    [InlineData(EventType.LoginFailed, KillChainPhase.Exploitation, "brute-force")]
    [InlineData(EventType.LoginSuccess, KillChainPhase.Exploitation, "brute-force")]
    [InlineData(EventType.Command, KillChainPhase.Installation, "post-exploitation")]
    [InlineData(EventType.HttpRequest, KillChainPhase.Recon, "web-scan")]
    [InlineData(EventType.Connect, KillChainPhase.Recon, "recon")]
    public void Classify_maps_event_type_to_expected_phase_and_category(
        EventType type, KillChainPhase expectedPhase, string expectedCategory)
    {
        var result = StubClassifier.Classify(TestEvents.Event("1.2.3.4", type));

        Assert.Equal(expectedPhase, result.KillChainPhase);
        Assert.Equal(expectedCategory, result.Category);
    }

    [Fact]
    public void Classify_sophistication_is_within_unit_range()
    {
        foreach (EventType type in Enum.GetValues<EventType>())
        {
            var result = StubClassifier.Classify(TestEvents.Event("1.2.3.4", type));
            Assert.InRange(result.Sophistication ?? -1, 0.0, 1.0);
            Assert.False(string.IsNullOrWhiteSpace(result.Intent));
        }
    }
}
