using HoneyGrid.Contracts;
using HoneyGrid.Functions.Ai;

namespace HoneyGrid.Functions.Tests;

public class ClassificationResponseParserTests
{
    [Fact]
    public void Parses_well_formed_array_and_maps_all_fields()
    {
        const string json = """
            [
              {"killChainPhase":"exploitation","category":"brute-force","sophistication":0.3,"intent":"przejęcie konta"},
              {"killChainPhase":"c2","category":"botnet","sophistication":0.9,"intent":"sterowanie botnetem"}
            ]
            """;

        var result = ClassificationResponseParser.Parse(json, 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(KillChainPhase.Exploitation, result[0]!.KillChainPhase);
        Assert.Equal("brute-force", result[0]!.Category);
        Assert.Equal(0.3, result[0]!.Sophistication);
        Assert.Equal(KillChainPhase.C2, result[1]!.KillChainPhase);
    }

    [Fact]
    public void Strips_markdown_code_fence()
    {
        const string fenced = "```json\n[{\"killChainPhase\":\"recon\",\"category\":\"web-scan\",\"sophistication\":0.1,\"intent\":\"skan\"}]\n```";

        var result = ClassificationResponseParser.Parse(fenced, 1);

        Assert.Single(result);
        Assert.Equal(KillChainPhase.Recon, result[0]!.KillChainPhase);
    }

    [Fact]
    public void Pads_missing_elements_with_null()
    {
        const string json = "[{\"killChainPhase\":\"recon\",\"category\":\"recon\",\"sophistication\":0.1,\"intent\":\"x\"}]";

        var result = ClassificationResponseParser.Parse(json, 3);

        Assert.Equal(3, result.Count);
        Assert.NotNull(result[0]);
        Assert.Null(result[1]);
        Assert.Null(result[2]);
    }

    [Fact]
    public void Garbage_input_yields_all_nulls()
    {
        var result = ClassificationResponseParser.Parse("przepraszam, nie mogę pomóc", 2);

        Assert.Equal(2, result.Count);
        Assert.All(result, Assert.Null);
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.42, 0.42)]
    public void Clamps_sophistication_to_unit_range(double input, double expected)
    {
        var json = $"[{{\"killChainPhase\":\"recon\",\"category\":\"x\",\"sophistication\":{input.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"intent\":\"y\"}}]";

        var result = ClassificationResponseParser.Parse(json, 1);

        Assert.Equal(expected, result[0]!.Sophistication);
    }

    [Fact]
    public void Sophistication_as_string_is_parsed()
    {
        const string json = "[{\"killChainPhase\":\"recon\",\"category\":\"x\",\"sophistication\":\"0.7\",\"intent\":\"y\"}]";

        var result = ClassificationResponseParser.Parse(json, 1);

        Assert.Equal(0.7, result[0]!.Sophistication);
    }

    [Theory]
    [InlineData("reconnaissance", KillChainPhase.Recon)]
    [InlineData("command-and-control", KillChainPhase.C2)]
    [InlineData("actions-on-objectives", KillChainPhase.Actions)]
    [InlineData("EXPLOITATION", KillChainPhase.Exploitation)]
    [InlineData("persistence", KillChainPhase.Installation)]
    public void ParsePhase_handles_synonyms_and_casing(string raw, KillChainPhase expected)
    {
        Assert.Equal(expected, ClassificationResponseParser.ParsePhase(raw));
    }

    [Fact]
    public void ParsePhase_returns_null_for_unknown()
    {
        Assert.Null(ClassificationResponseParser.ParsePhase("banana"));
        Assert.Null(ClassificationResponseParser.ParsePhase(null));
    }

    [Fact]
    public void Extract_json_array_finds_outer_brackets()
    {
        Assert.Equal("[1, 2]", ClassificationResponseParser.ExtractJsonArray("oto wynik: [1, 2] gotowe"));
        Assert.Null(ClassificationResponseParser.ExtractJsonArray("brak tablicy"));
    }
}
