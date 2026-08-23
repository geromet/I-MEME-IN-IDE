using MemeSearcher.Infrastructure.Alignment;

namespace MemeSearcher.Tests.Alignment;

/// <summary>
/// MFA isn't installed on the machine these tests run on, so this can't be verified against real
/// MFA output. Built against Praat's documented "long text" TextGrid format instead - a stable,
/// extensively documented format MFA outputs directly rather than a format of its own invention.
/// </summary>
public class TextGridParserTests
{
    private const string SampleTextGrid = """
        File type = "ooTextFile"
        Object class = "TextGrid"

        xmin = 0.0
        xmax = 2.5
        tiers? <exists>
        size = 2
        item []:
            item [1]:
                class = "IntervalTier"
                name = "words"
                xmin = 0.0
                xmax = 2.5
                intervals: size = 3
                intervals [1]:
                    xmin = 0.0
                    xmax = 0.5
                    text = ""
                intervals [2]:
                    xmin = 0.5
                    xmax = 1.2
                    text = "hello"
                intervals [3]:
                    xmin = 1.2
                    xmax = 2.5
                    text = "world"
            item [2]:
                class = "IntervalTier"
                name = "phones"
                xmin = 0.0
                xmax = 2.5
                intervals: size = 5
                intervals [1]:
                    xmin = 0.0
                    xmax = 0.5
                    text = "sil"
                intervals [2]:
                    xmin = 0.5
                    xmax = 0.8
                    text = "HH"
                intervals [3]:
                    xmin = 0.8
                    xmax = 1.2
                    text = "AH0"
                intervals [4]:
                    xmin = 1.2
                    xmax = 1.8
                    text = "W"
                intervals [5]:
                    xmin = 1.8
                    xmax = 2.5
                    text = "ER1"
        """;

    [Fact]
    public void Parse_ExtractsBothTiersWithCorrectIntervalCounts()
    {
        var tiers = TextGridParser.Parse(SampleTextGrid);

        Assert.True(tiers.ContainsKey("words"));
        Assert.True(tiers.ContainsKey("phones"));
        Assert.Equal(3, tiers["words"].Count);
        Assert.Equal(5, tiers["phones"].Count);
    }

    [Fact]
    public void Parse_DoesNotConfuseTierLevelXminXmaxWithAnInterval()
    {
        // The tier header itself has an xmin/xmax pair (0.0/2.5) before "intervals: size = N" -
        // this must not be mistaken for interval [1].
        var tiers = TextGridParser.Parse(SampleTextGrid);

        Assert.Equal(0.0, tiers["words"][0].StartSeconds);
        Assert.Equal(0.5, tiers["words"][0].EndSeconds);
        Assert.Equal("", tiers["words"][0].Text);
    }

    [Fact]
    public void Parse_ExtractsCorrectTimingAndTextForEachInterval()
    {
        var tiers = TextGridParser.Parse(SampleTextGrid);

        Assert.Equal(0.5, tiers["words"][1].StartSeconds);
        Assert.Equal(1.2, tiers["words"][1].EndSeconds);
        Assert.Equal("hello", tiers["words"][1].Text);

        Assert.Equal(1.2, tiers["words"][2].StartSeconds);
        Assert.Equal(2.5, tiers["words"][2].EndSeconds);
        Assert.Equal("world", tiers["words"][2].Text);

        Assert.Equal("HH", tiers["phones"][1].Text);
        Assert.Equal("ER1", tiers["phones"][4].Text);
    }

    [Fact]
    public void Parse_EmptyOrMalformedContentReturnsNoTiers()
    {
        Assert.Empty(TextGridParser.Parse(""));
        Assert.Empty(TextGridParser.Parse("not a textgrid at all"));
    }
}
