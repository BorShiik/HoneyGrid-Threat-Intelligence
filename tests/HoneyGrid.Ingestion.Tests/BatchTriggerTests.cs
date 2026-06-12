using HoneyGrid.Ingestion.Sinks;

namespace HoneyGrid.Ingestion.Tests;

/// <summary>Logika decyzji o flushu paczki Service Bus (rozmiar LUB czas).</summary>
public sealed class BatchTriggerTests
{
    private static readonly BatchTrigger Trigger = new(50, TimeSpan.FromSeconds(5));

    [Fact]
    public void FullBatch_TriggersFlush_RegardlessOfElapsedTime()
    {
        Assert.True(Trigger.ShouldFlush(50, TimeSpan.Zero));
        Assert.True(Trigger.ShouldFlush(75, TimeSpan.Zero)); // powyżej limitu też
    }

    [Fact]
    public void ElapsedInterval_TriggersFlush_ForPartialBatch()
    {
        Assert.True(Trigger.ShouldFlush(1, TimeSpan.FromSeconds(5)));
        Assert.True(Trigger.ShouldFlush(49, TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public void PartialBatch_BeforeInterval_DoesNotFlush()
    {
        Assert.False(Trigger.ShouldFlush(49, TimeSpan.FromSeconds(4.9)));
        Assert.False(Trigger.ShouldFlush(1, TimeSpan.Zero));
    }

    [Fact]
    public void EmptyBuffer_NeverFlushes_EvenAfterInterval()
    {
        Assert.False(Trigger.ShouldFlush(0, TimeSpan.FromMinutes(10)));
        Assert.False(Trigger.ShouldFlush(0, TimeSpan.Zero));
    }
}
