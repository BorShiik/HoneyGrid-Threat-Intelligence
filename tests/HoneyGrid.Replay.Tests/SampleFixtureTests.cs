namespace HoneyGrid.Replay.Tests;

/// <summary>
/// Weryfikuje, że fizyczna próbka fixtures/tty/sample.tty jest spójna z generatorem
/// w teście (ten sam układ bajtów) i parsuje się do oczekiwanej sesji.
/// </summary>
public sealed class SampleFixtureTests
{
    [Fact]
    public void OnDiskFixture_MatchesGeneratedSample()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.tty");
        Assert.True(File.Exists(path), $"brak fikstury: {path}");

        var onDisk = File.ReadAllBytes(path);
        var generated = TtyFixtureBuilder.BuildLoginSample();

        Assert.Equal(generated, onDisk);
    }

    [Fact]
    public void OnDiskFixture_ParsesToFiveFramesEndingWithPrompt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.tty");
        var frames = TtyParser.Parse(File.ReadAllBytes(path));

        Assert.Equal(5, frames.Count);
        Assert.Equal("# ", frames[^1].Data);
        Assert.Equal(2250, frames[^1].OffsetMs);
    }
}
