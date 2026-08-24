using System.Collections.Generic;
using System.Linq;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #15's exit criteria, exercised directly against the view model: an MFA-aligned source shows
/// real per-phone blocks, a transcript-only source shows the estimated fallback clearly labelled,
/// and substitution/insertion positions are visible against the query.
/// </summary>
public class InspectorViewModelTests
{
    private static FFmpegClipExtractor MakeClipExtractor() => new(new FFmpegToolLocator());

    private static SearchResultRowViewModel MakeRow(
        IReadOnlyList<MatchedPhone> matchedPhoneDetails,
        IReadOnlyList<QueryAlignmentStep>? alignmentSteps = null,
        string? mediaPath = "/media/clip.mp4",
        IReadOnlyList<string>? queryPhonemes = null,
        int queryStart = 0,
        int queryEnd = 0)
    {
        var result = new SearchResult(
            MediaId: Guid.NewGuid(),
            StartSeconds: 1.0,
            EndSeconds: 2.0,
            SourceText: "hello",
            Ipa: "hɛloʊ",
            Phonemes: matchedPhoneDetails.Select(p => p.Symbol).ToList(),
            QueryPhonemes: queryPhonemes ?? matchedPhoneDetails.Select(p => p.Symbol).ToList(),
            Score: 0.9,
            MatchedPhoneDetails: matchedPhoneDetails,
            AlignmentSteps: alignmentSteps ?? [],
            QueryStart: queryStart,
            QueryEnd: queryEnd);

        return new SearchResultRowViewModel(
            result, new FakeMediaPlayerLauncher(), new FakeClipboardService(), MakeClipExtractor(), new FakeFilePickerService())
        {
            MediaPath = mediaPath,
        };
    }

    [Fact]
    public void Show_NoSelection_ReportsNoSelection()
    {
        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());

        inspector.Show(null);

        Assert.False(inspector.HasSelection);
        Assert.Empty(inspector.PhoneBlocks);
    }

    /// <summary>Exit criterion: "a result from an MFA-aligned source shows real per-phone blocks."</summary>
    [Fact]
    public void Show_AllPhonesPhoneLevelAligned_ReportsPreciseAlignment()
    {
        var row = MakeRow([
            new MatchedPhone("h", 1.0, 1.1, IsPhoneLevelAligned: true),
            new MatchedPhone("ɛ", 1.1, 1.3, IsPhoneLevelAligned: true),
        ]);

        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.Show(row);

        Assert.True(inspector.HasSelection);
        Assert.Equal(2, inspector.PhoneBlocks.Count);
        Assert.All(inspector.PhoneBlocks, b => Assert.True(b.IsAligned));
        Assert.Contains("Precisely aligned", inspector.AlignmentSummary);
    }

    /// <summary>Exit criterion: "a transcript-only source shows the estimated-timing fallback, clearly labelled as such."</summary>
    [Fact]
    public void Show_NoPhonesPhoneLevelAligned_ReportsEstimatedTiming()
    {
        var row = MakeRow([
            new MatchedPhone("h", 1.0, 1.5, IsPhoneLevelAligned: false),
            new MatchedPhone("ɛ", 1.0, 1.5, IsPhoneLevelAligned: false),
        ]);

        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.Show(row);

        Assert.All(inspector.PhoneBlocks, b => Assert.False(b.IsAligned));
        Assert.Contains("Estimated", inspector.AlignmentSummary);
    }

    [Fact]
    public void Show_MixOfAlignedAndEstimatedPhones_ReportsPartialAlignment()
    {
        var row = MakeRow([
            new MatchedPhone("h", 1.0, 1.1, IsPhoneLevelAligned: true),
            new MatchedPhone("ɛ", 1.1, 1.3, IsPhoneLevelAligned: false),
        ]);

        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.Show(row);

        Assert.Contains("Partially aligned", inspector.AlignmentSummary);
    }

    /// <summary>Exit criterion: "substitution/insertion positions visible against the query" - now via the shared coverage strip (#25) rather than a flat WrapPanel of chips.</summary>
    [Fact]
    public void Show_AlignmentSteps_SurfacesSubstitutionsAndInsertions()
    {
        var row = MakeRow(
            [new MatchedPhone("h", 1.0, 1.1, true), new MatchedPhone("ɛ", 1.1, 1.3, true)],
            [
                new QueryAlignmentStep(AlignmentOp.Match, "h", "h", QueryIndex: 0),
                new QueryAlignmentStep(AlignmentOp.Substitute, "ə", "ɛ", QueryIndex: 1),
                // Outside the covered span - the query asked for "z" and this candidate has
                // nothing corresponding to it at all, not even a substitution.
                new QueryAlignmentStep(AlignmentOp.QueryExtra, "z", null, QueryIndex: 2),
            ],
            queryPhonemes: ["h", "ə", "z"],
            queryStart: 0,
            queryEnd: 2);

        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.Show(row);

        Assert.True(inspector.HasAlignmentSteps);
        var cells = inspector.CoverageStrip.Cells;
        Assert.Equal(3, cells.Count);
        Assert.True(cells[0].IsMatch);
        Assert.True(cells[1].IsSubstitute);
        Assert.True(cells[2].IsOutsideSpan);
    }

    /// <summary>
    /// #25 review finding: a query-indexed coverage strip cannot place AlignmentOp.CandidateExtra
    /// at all (it consumes no query position) - this phone-has-more-than-the-query signal must
    /// keep surfacing somewhere, or it silently disappears from the app entirely.
    /// </summary>
    [Fact]
    public void Show_CandidateExtraStep_StillSurfacesAsAnExtraPhoneme()
    {
        var row = MakeRow(
            [new MatchedPhone("h", 1.0, 1.1, true)],
            [
                new QueryAlignmentStep(AlignmentOp.Match, "h", "h", QueryIndex: 0),
                new QueryAlignmentStep(AlignmentOp.CandidateExtra, null, "t"),
            ],
            queryPhonemes: ["h"],
            queryStart: 0,
            queryEnd: 1);

        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.Show(row);

        Assert.True(inspector.HasExtraPhonemes);
        Assert.Equal("+t", Assert.Single(inspector.ExtraPhonemes));
    }

    /// <summary>Click-to-seek: extends the existing external-player launch to a specific phone's own start time.</summary>
    [Fact]
    public async Task SeekCommand_OpensTheMediaAtThePhonesOwnStartTime()
    {
        var row = MakeRow([new MatchedPhone("h", 3.5, 3.7, true)]);
        var launcher = new FakeMediaPlayerLauncher();
        var inspector = new InspectorViewModel(launcher);
        inspector.Show(row);

        var block = Assert.Single(inspector.PhoneBlocks);
        await inspector.SeekCommand.ExecuteAsync(block);

        Assert.Equal("/media/clip.mp4", launcher.LastMediaPath);
        Assert.Equal(3.5, launcher.LastStartSeconds);
    }

    [Fact]
    public async Task SeekCommand_WithNoTiming_DoesNothing()
    {
        var row = MakeRow([new MatchedPhone("h", null, null, false)]);
        var launcher = new FakeMediaPlayerLauncher();
        var inspector = new InspectorViewModel(launcher);
        inspector.Show(row);

        var block = Assert.Single(inspector.PhoneBlocks);
        await inspector.SeekCommand.ExecuteAsync(block);

        Assert.Equal(0, launcher.CallCount);
    }
}
