using HoneyGrid.Functions.Profiling;

namespace HoneyGrid.Functions.Tests;

public class ActorNameGeneratorTests
{
    [Fact]
    public void Same_seed_yields_same_name_and_id()
    {
        const string seed = "1.1.1.1|2.2.2.2";
        Assert.Equal(ActorNameGenerator.Generate(seed), ActorNameGenerator.Generate(seed));
        Assert.Equal(ActorNameGenerator.GenerateId(seed), ActorNameGenerator.GenerateId(seed));
    }

    [Fact]
    public void Name_has_adjective_and_noun()
    {
        var name = ActorNameGenerator.Generate("seed");
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, parts.Length);
    }

    [Fact]
    public void Id_has_expected_prefix_and_shape()
    {
        var id = ActorNameGenerator.GenerateId("1.1.1.1");
        Assert.StartsWith("actor-", id);
        Assert.Equal("actor-".Length + 8, id.Length);
    }

    [Fact]
    public void Different_seeds_usually_differ()
    {
        Assert.NotEqual(ActorNameGenerator.GenerateId("1.1.1.1"), ActorNameGenerator.GenerateId("9.9.9.9"));
    }
}
