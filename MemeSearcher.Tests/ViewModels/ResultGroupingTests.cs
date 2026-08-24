using System;
using System.Linq;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #25 exit criterion 4: "many hits are the same word from different timestamps - grouping by
/// covered span is more useful than a flat ranked list." Exercises the pure grouping rule directly,
/// same pattern as ResultSortFilterTests - no database or espeak, just hand-built rows.
/// </summary>
public class ResultGroupingTests
{
    private static readonly string[] QueryPhonemes = ["m", "u", "t", "ə", "n"];

    private static SearchResultRowViewModel Row(string sourceText, int queryStart, int queryEnd, double score = 0.8)
    {
        var alignmentSteps = QueryPhonemes
            .Select((symbol, i) => i >= queryStart && i < queryEnd
                ? new QueryAlignmentStep(AlignmentOp.Match, symbol, symbol, QueryIndex: i)
                : new QueryAlignmentStep(AlignmentOp.QueryExtra, symbol, null, QueryIndex: i))
            .ToList();

        var result = new SearchResult(
            MediaId: Guid.NewGuid(),
            StartSeconds: 0,
            EndSeconds: 1,
            SourceText: sourceText,
            Ipa: sourceText,
            Phonemes: QueryPhonemes.Skip(queryStart).Take(queryEnd - queryStart).ToList(),
            Score: score,
            QueryPhonemes: QueryPhonemes,
            AlignmentSteps: alignmentSteps,
            QueryStart: queryStart,
            QueryEnd: queryEnd);

        return new SearchResultRowViewModel(
            result, new FakeMediaPlayerLauncher(), new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService());
    }

    [Fact]
    public void GroupByCoveredSpan_SameSpanDifferentWords_GroupsTogether()
    {
        // "maken" and "laten" both covering positions [0, 2) of "moeten" - the exact scenario the
        // issue names: unrelated-looking words that are actually competing candidates for the same slice.
        var maken = Row("maken", 0, 2);
        var laten = Row("laten", 0, 2);

        var groups = ResultGrouping.GroupByCoveredSpan([maken, laten]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Contains(maken, group.Members);
        Assert.Contains(laten, group.Members);
    }

    [Fact]
    public void GroupByCoveredSpan_DifferentSpans_ProduceSeparateGroups()
    {
        var prefix = Row("mu", 0, 2);
        var suffix = Row("tan", 2, 5);

        var groups = ResultGrouping.GroupByCoveredSpan([prefix, suffix]);

        Assert.Equal(2, groups.Count);
        Assert.Single(groups[0].Members);
        Assert.Single(groups[1].Members);
    }

    [Fact]
    public void GroupByCoveredSpan_OrdersGroupsByQueryStartAscending()
    {
        var late = Row("tan", 2, 5);
        var early = Row("mu", 0, 2);

        // Deliberately passed in the "wrong" (score/arbitrary) order - grouping must still read
        // left to right across the query regardless of input order.
        var groups = ResultGrouping.GroupByCoveredSpan([late, early]);

        Assert.Equal(early, groups[0].Members[0]);
        Assert.Equal(late, groups[1].Members[0]);
    }

    [Fact]
    public void GroupByCoveredSpan_PreservesInputOrderWithinAGroup()
    {
        // Simulates rows having already been sorted upstream by ResultSortFilter - grouping must
        // not re-rank them.
        var higherScore = Row("laten", 0, 2, score: 0.9);
        var lowerScore = Row("maken", 0, 2, score: 0.6);

        var groups = ResultGrouping.GroupByCoveredSpan([higherScore, lowerScore]);

        Assert.Equal([higherScore, lowerScore], Assert.Single(groups).Members);
    }

    [Fact]
    public void GroupByCoveredSpan_LabelsAGroupWithTheCoveredSlice()
    {
        var group = Assert.Single(ResultGrouping.GroupByCoveredSpan([Row("maken", 0, 2)]));

        Assert.Equal("Covers \"m u\"", group.Label);
    }
}
