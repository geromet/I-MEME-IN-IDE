using System.Linq;
using MemeSearcher.Core.Search;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #25's core visual guarantee: one cell per query phoneme regardless of what this match says
/// about it, colored into exactly four states - never flattened to a covered/uncovered boolean,
/// since the issue's own warning is that collapsing "covered but substituted" into "covered" is
/// how a bad match looks good.
/// </summary>
public class PhoneCoverageStripBuilderTests
{
    [Fact]
    public void Build_OneCellPerQueryPhoneme_RegardlessOfHowManyStepsExist()
    {
        var cells = PhoneCoverageStripBuilder.Build(["m", "u", "t", "ə", "n"], [], queryStart: 0, queryEnd: 0);

        Assert.Equal(5, cells.Count);
        Assert.Equal(["m", "u", "t", "ə", "n"], cells.Select(c => c.Symbol));
    }

    [Fact]
    public void Build_MatchStep_IsExactlyMatch()
    {
        var cells = PhoneCoverageStripBuilder.Build(
            ["m"], [new QueryAlignmentStep(AlignmentOp.Match, "m", "m", QueryIndex: 0)], queryStart: 0, queryEnd: 1);

        Assert.True(Assert.Single(cells).IsMatch);
    }

    [Fact]
    public void Build_SubstituteStep_IsExactlySubstitute()
    {
        var cells = PhoneCoverageStripBuilder.Build(
            ["m"], [new QueryAlignmentStep(AlignmentOp.Substitute, "m", "n", QueryIndex: 0)], queryStart: 0, queryEnd: 1);

        Assert.True(Assert.Single(cells).IsSubstitute);
    }

    /// <summary>The "moeten" case's actual shape: a short onset genuinely covered, and everything past it entirely missing from this candidate - not a substitution, a real absence.</summary>
    [Fact]
    public void Build_QueryExtraOutsideTheCoveredSpan_IsOutsideSpanNotGap()
    {
        var steps = new[]
        {
            new QueryAlignmentStep(AlignmentOp.Match, "m", "m", QueryIndex: 0),
            new QueryAlignmentStep(AlignmentOp.QueryExtra, "u", null, QueryIndex: 1),
            new QueryAlignmentStep(AlignmentOp.QueryExtra, "t", null, QueryIndex: 2),
        };

        // Only position 0 is actually covered - the span is [0, 1), not the full query.
        var cells = PhoneCoverageStripBuilder.Build(["m", "u", "t"], steps, queryStart: 0, queryEnd: 1);

        Assert.True(cells[0].IsMatch);
        Assert.True(cells[1].IsOutsideSpan);
        Assert.True(cells[2].IsOutsideSpan);
    }

    /// <summary>A hole *inside* an otherwise-covered stretch reads differently from being trimmed off the end - it's the interior-gap state the issue explicitly warns not to collapse into "covered".</summary>
    [Fact]
    public void Build_QueryExtraInsideTheCoveredSpan_IsGapWithinSpan()
    {
        var steps = new[]
        {
            new QueryAlignmentStep(AlignmentOp.Match, "m", "m", QueryIndex: 0),
            new QueryAlignmentStep(AlignmentOp.QueryExtra, "u", null, QueryIndex: 1),
            new QueryAlignmentStep(AlignmentOp.Match, "t", "t", QueryIndex: 2),
        };

        // The span envelopes all three positions even though the middle one has no candidate phone.
        var cells = PhoneCoverageStripBuilder.Build(["m", "u", "t"], steps, queryStart: 0, queryEnd: 3);

        Assert.True(cells[0].IsMatch);
        Assert.True(cells[1].IsGap);
        Assert.True(cells[2].IsMatch);
    }

    [Fact]
    public void Build_CandidateExtraStep_IsIgnored_SinceItDoesNotConsumeAQueryPosition()
    {
        var steps = new[]
        {
            new QueryAlignmentStep(AlignmentOp.Match, "m", "m", QueryIndex: 0),
            new QueryAlignmentStep(AlignmentOp.CandidateExtra, null, "z"),
        };

        var cells = PhoneCoverageStripBuilder.Build(["m"], steps, queryStart: 0, queryEnd: 1);

        Assert.Single(cells);
        Assert.True(cells[0].IsMatch);
    }
}
