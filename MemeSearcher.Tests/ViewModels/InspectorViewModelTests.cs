using System.Collections.Generic;
using System.Linq;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

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

    private static CompositeSearchResultRowViewModel MakeCompositeRow()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var result = new CompositeSearchResult(
            0.88,
            [
                new CompositeMatchComponent(
                    firstId, 1.0, 1.4, "super", "suːpər", ["s", "u"], 0.9, 0, 2,
                    [new MatchedPhone("s", 1.0, 1.1, true), new MatchedPhone("u", 1.1, 1.3, true)]),
                new CompositeMatchComponent(
                    secondId, 8.0, 8.5, "man", "mæn", ["m", "æ"], 0.85, 2, 4,
                    [new MatchedPhone("m", 8.0, 8.2, true), new MatchedPhone("æ", 8.2, 8.4, false)]),
            ],
            ["s", "u", "m", "æ"]);

        return new CompositeSearchResultRowViewModel(
            result,
            new Dictionary<Guid, string> { [firstId] = "Source A", [secondId] = "Source B" },
            new Dictionary<Guid, string> { [firstId] = "/media/a.mp4", [secondId] = "/media/b.mp4" },
            MakeClipExtractor(),
            new FakeFilePickerService());
    }

    [Fact]
    public void Show_NoSelection_ReportsNoSelection()
    {
        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.Show(null);
        Assert.False(inspector.HasSelection);
        Assert.Empty(inspector.PhoneBlocks);
    }

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
        Assert.True(inspector.IsSingleSelection);
        Assert.Equal(2, inspector.PhoneBlocks.Count);
        Assert.All(inspector.PhoneBlocks, b => Assert.True(b.IsAligned));
        Assert.Contains("Precisely aligned", inspector.AlignmentSummary);
    }

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

    [Fact]
    public void Show_AlignmentSteps_SurfacesSubstitutionsAndInsertions()
    {
        var row = MakeRow(
            [new MatchedPhone("h", 1.0, 1.1, true), new MatchedPhone("ɛ", 1.1, 1.3, true)],
            [
                new QueryAlignmentStep(AlignmentOp.Match, "h", "h", QueryIndex: 0),
                new QueryAlignmentStep(AlignmentOp.Substitute, "ə", "ɛ", QueryIndex: 1),
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

    [Fact]
    public void ShowComposite_RendersEveryComponentInAssemblyOrderWithExistingCoverageAndAlignment()
    {
        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.ShowComposite(MakeCompositeRow());

        Assert.True(inspector.HasSelection);
        Assert.True(inspector.IsCompositeSelection);
        Assert.False(inspector.IsSingleSelection);
        Assert.Equal(2, inspector.CompositeComponents.Count);

        var first = inspector.CompositeComponents[0];
        Assert.Equal("COMPONENT 1", first.OrdinalDisplay);
        Assert.Equal("Source A", first.MediaTitle);
        Assert.Equal("Query phones 1-2", first.QueryCoverageDisplay);
        Assert.Contains("Precisely aligned", first.AlignmentSummary);

        var second = inspector.CompositeComponents[1];
        Assert.Equal("COMPONENT 2", second.OrdinalDisplay);
        Assert.Equal("Source B", second.MediaTitle);
        Assert.Equal("Query phones 3-4", second.QueryCoverageDisplay);
        Assert.Contains("Partially aligned", second.AlignmentSummary);
    }

    [Fact]
    public async Task SeekCompositeCommand_UsesTheSelectedComponentsOwnMediaAndPhoneTimestamp()
    {
        var launcher = new FakeMediaPlayerLauncher();
        var inspector = new InspectorViewModel(launcher);
        inspector.ShowComposite(MakeCompositeRow());

        var secondComponent = inspector.CompositeComponents[1];
        var phone = secondComponent.Phones[1];
        await inspector.SeekCompositeCommand.ExecuteAsync(phone);

        Assert.Equal("/media/b.mp4", launcher.LastMediaPath);
        Assert.Equal(8.2, launcher.LastStartSeconds);
        Assert.Contains("Source B", inspector.SeekStatus);
    }

    [Fact]
    public void ShowSingleAfterComposite_ClearsCompositePresentationWithoutRegressingSingleMode()
    {
        var inspector = new InspectorViewModel(new FakeMediaPlayerLauncher());
        inspector.ShowComposite(MakeCompositeRow());
        inspector.Show(MakeRow([new MatchedPhone("h", 1.0, 1.1, true)]));

        Assert.True(inspector.IsSingleSelection);
        Assert.False(inspector.IsCompositeSelection);
        Assert.Empty(inspector.CompositeComponents);
        Assert.Single(inspector.PhoneBlocks);
    }
}
