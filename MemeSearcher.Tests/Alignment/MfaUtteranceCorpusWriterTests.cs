using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Alignment;

namespace MemeSearcher.Tests.Alignment;

/// <summary>
/// #33: proves the TextGrid-corpus input MFA is fed round-trips through the same parser used to
/// read MFA's own output (TextGridParser), since mfa itself isn't installed on this machine to
/// verify against directly.
/// </summary>
public class MfaUtteranceCorpusWriterTests
{
    [Fact]
    public void Write_SingleUtteranceCoveringTheWholeFile_ProducesOneInterval()
    {
        var textGrid = MfaUtteranceCorpusWriter.Write([new AlignmentUtterance(0, 3.0, "hello world")], 3.0);

        var tiers = TextGridParser.Parse(textGrid);
        var intervals = tiers["utterances"];

        var interval = Assert.Single(intervals);
        Assert.Equal(0, interval.StartSeconds);
        Assert.Equal(3.0, interval.EndSeconds);
        Assert.Equal("hello world", interval.Text);
    }

    /// <summary>The actual #33 fix: a multi-segment transcript becomes multiple utterance intervals, with silence intervals filling the gaps - not one blob.</summary>
    [Fact]
    public void Write_MultipleUtterancesWithGaps_TilesTheWholeSpanWithNoOverlaps()
    {
        var utterances = new[]
        {
            new AlignmentUtterance(1.0, 3.0, "hello"),
            new AlignmentUtterance(5.0, 7.0, "world"),
        };

        var textGrid = MfaUtteranceCorpusWriter.Write(utterances, 10.0);
        var intervals = TextGridParser.Parse(textGrid)["utterances"];

        // silence, hello, silence, world, silence - contiguous, no gaps or overlaps.
        Assert.Equal(5, intervals.Count);
        Assert.Equal((0.0, 1.0, ""), (intervals[0].StartSeconds, intervals[0].EndSeconds, intervals[0].Text));
        Assert.Equal((1.0, 3.0, "hello"), (intervals[1].StartSeconds, intervals[1].EndSeconds, intervals[1].Text));
        Assert.Equal((3.0, 5.0, ""), (intervals[2].StartSeconds, intervals[2].EndSeconds, intervals[2].Text));
        Assert.Equal((5.0, 7.0, "world"), (intervals[3].StartSeconds, intervals[3].EndSeconds, intervals[3].Text));
        Assert.Equal((7.0, 10.0, ""), (intervals[4].StartSeconds, intervals[4].EndSeconds, intervals[4].Text));

        for (var i = 1; i < intervals.Count; i++)
        {
            Assert.Equal(intervals[i - 1].EndSeconds, intervals[i].StartSeconds);
        }
    }

    [Fact]
    public void Write_UtterancesGivenOutOfOrder_AreSortedByStartTime()
    {
        var utterances = new[]
        {
            new AlignmentUtterance(5.0, 7.0, "second"),
            new AlignmentUtterance(1.0, 3.0, "first"),
        };

        var intervals = TextGridParser.Parse(MfaUtteranceCorpusWriter.Write(utterances, 10.0))["utterances"];

        Assert.Equal("first", intervals[1].Text);
        Assert.Equal("second", intervals[3].Text);
    }

    /// <summary>An utterance whose clamped span collapses to nothing (e.g. entirely covered by an earlier one) must not produce a zero/negative-length interval that would corrupt the tier's tiling.</summary>
    [Fact]
    public void Write_OverlappingUtterance_IsDroppedRatherThanProducingAnInvalidInterval()
    {
        var utterances = new[]
        {
            new AlignmentUtterance(0, 5.0, "first"),
            new AlignmentUtterance(1.0, 2.0, "entirely inside first"),
        };

        var intervals = TextGridParser.Parse(MfaUtteranceCorpusWriter.Write(utterances, 5.0))["utterances"];

        var interval = Assert.Single(intervals);
        Assert.Equal("first", interval.Text);
    }

    [Fact]
    public void Write_TextContainingAQuote_EscapesItForPraatsFormat()
    {
        var textGrid = MfaUtteranceCorpusWriter.Write([new AlignmentUtterance(0, 1.0, "she said \"hi\"")], 1.0);

        Assert.Contains("text = \"she said \"\"hi\"\"\"", textGrid);
    }

    [Fact]
    public void Write_NonPositiveDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MfaUtteranceCorpusWriter.Write([], 0));
    }
}
