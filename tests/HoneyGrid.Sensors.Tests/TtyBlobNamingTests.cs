using HoneyGrid.Sensors.CowrieShipper;

namespace HoneyGrid.Sensors.Tests;

/// <summary>Testy czystej logiki nazewnictwa blobów / referencji TTY.</summary>
public sealed class TtyBlobNamingTests
{
    [Fact]
    public void BlobName_AppendsTtyExtension()
    {
        Assert.Equal("abc123.tty", TtyBlobNaming.BlobName("abc123"));
    }

    [Fact]
    public void TtyRef_CombinesContainerAndBlobName()
    {
        Assert.Equal("tty/abc123.tty", TtyBlobNaming.TtyRef("tty", "abc123"));
    }

    [Fact]
    public void Mapper_LogClosedWithTtylog_SetsLogicalTtyRef()
    {
        const string line = """
            {"eventid":"cowrie.log.closed","session":"sess-xyz","src_ip":"203.0.113.9","sensor":"ssh-eu-01","ttylog":"/cowrie/var/lib/cowrie/tty/abcdef0123","timestamp":"2026-06-13T10:00:00Z"}
            """;

        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Equal("tty/sess-xyz.tty", evt!.TtyRef);
    }

    [Fact]
    public void Mapper_LogClosedWithoutTtylog_LeavesTtyRefNull()
    {
        const string line = """
            {"eventid":"cowrie.log.closed","session":"sess-xyz","src_ip":"203.0.113.9","sensor":"ssh-eu-01","timestamp":"2026-06-13T10:00:00Z"}
            """;

        var evt = CowrieEventMapper.Map(line);

        Assert.NotNull(evt);
        Assert.Null(evt!.TtyRef);
    }
}
