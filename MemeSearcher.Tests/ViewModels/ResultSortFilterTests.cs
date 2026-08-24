using System;
using System.Linq;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #25 exit criterion 2: "coverage is a first-class sort/filter axis alongside score." Exercises
/// the pure ordering/filtering rule directly against hand-built rows - no database, no espeak, no
/// SearchViewModel plumbing - since what's under test is the rule itself, not the pipeline that
/// feeds it.
/// </summary>
public class ResultSortFilterTests
{
    private static SearchResultRowViewModel Row(string sourceText, double score, int coveredPositions, int totalQueryPhonemes)
    {
        var queryPhonemes = Enumerable.Range(0, totalQueryPhonemes).Select(i => $"p{i}").ToList();
        var alignmentSteps = queryPhonemes
            .Select((symbol, i) => i < coveredPositions
                ? new QueryAlignmentStep(AlignmentOp.Match, symbol, symbol, QueryIndex: i)
                : new QueryAlignmentStep(AlignmentOp.QueryExtra, symbol, null, QueryIndex: i))
            .ToList();

        var result = new SearchResult(
            MediaId: Guid.NewGuid(),
            StartSeconds: 0,
            EndSeconds: 1,
            SourceText: sourceText,
            Ipa: sourceText,
            Phonemes: queryPhonemes.Take(coveredPositions).ToList(),
            Score: score,
            QueryPhonemes: queryPhonemes,
            AlignmentSteps: alignmentSteps,
            QueryStart: 0,
            QueryEnd: coveredPositions);

        return new SearchResultRowViewModel(
            result, new FakeMediaPlayerLauncher(), new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService());
    }

    [Fact]
    public void Apply_ScoreMode_LeavesTheServersOrderUntouched()
    {
        // Deliberately NOT sorted by score - Score mode must pass the sequence through as-is,
        // trusting whatever order the caller already has, rather than re-deriving it.
        var low = Row("low", score: 0.3, coveredPositions: 2, totalQueryPhonemes: 4);
        var high = Row("high", score: 0.9, coveredPositions: 4, totalQueryPhonemes: 4);

        var result = ResultSortFilter.Apply([low, high], ResultSortMode.Score, minimumCoverage: 0).ToList();

        Assert.Equal([low, high], result);
    }

    [Fact]
    public void Apply_CoverageMode_SortsByCoverageDescendingThenScore()
    {
        // "full" scores lower than "partial" despite covering the whole query - the exact scenario
        // exit criterion 2 exists for: score alone would rank "partial" first.
        var full = Row("full", score: 0.6, coveredPositions: 4, totalQueryPhonemes: 4); // coverage 1.0
        var partial = Row("partial", score: 0.9, coveredPositions: 1, totalQueryPhonemes: 4); // coverage 0.25

        var result = ResultSortFilter.Apply([partial, full], ResultSortMode.Coverage, minimumCoverage: 0).ToList();

        Assert.Equal([full, partial], result);
    }

    [Fact]
    public void Apply_CoverageMode_BreaksTiesByScore()
    {
        var lowerScore = Row("a", score: 0.5, coveredPositions: 2, totalQueryPhonemes: 4);
        var higherScore = Row("b", score: 0.8, coveredPositions: 2, totalQueryPhonemes: 4);

        var result = ResultSortFilter.Apply([lowerScore, higherScore], ResultSortMode.Coverage, minimumCoverage: 0).ToList();

        Assert.Equal([higherScore, lowerScore], result);
    }

    [Fact]
    public void Apply_MinimumCoverage_ExcludesRowsBelowTheThreshold()
    {
        var belowThreshold = Row("below", score: 0.9, coveredPositions: 1, totalQueryPhonemes: 4); // coverage 0.25
        var atThreshold = Row("at", score: 0.5, coveredPositions: 2, totalQueryPhonemes: 4); // coverage 0.5

        var result = ResultSortFilter.Apply([belowThreshold, atThreshold], ResultSortMode.Score, minimumCoverage: 0.5).ToList();

        Assert.Equal([atThreshold], result);
    }

    [Fact]
    public void Apply_MinimumCoverageZero_ExcludesNothing()
    {
        var zeroCoverage = Row("none", score: 0.9, coveredPositions: 0, totalQueryPhonemes: 4);

        var result = ResultSortFilter.Apply([zeroCoverage], ResultSortMode.Score, minimumCoverage: 0).ToList();

        Assert.Equal([zeroCoverage], result);
    }
}
