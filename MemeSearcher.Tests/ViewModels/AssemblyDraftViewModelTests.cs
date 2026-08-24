using System;
using System.Linq;
using System.Threading.Tasks;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #25 exit criterion 3: the manual assembly draft - one slot per covered span (built from
/// ResultGrouping's groups), the user picking a candidate per slot, audition/export gated on every
/// slot actually being fillable. No database or espeak needed; ffmpeg calls only happen once a slot
/// resolves to a real, timed media path, which these tests deliberately don't provide, so they
/// exercise the gating logic itself rather than real clip extraction.
/// </summary>
public class AssemblyDraftViewModelTests
{
    private static SearchResultRowViewModel Row(string sourceText, double score, string? mediaPath = null, double? start = null, double? end = null)
    {
        var result = new SearchResult(
            MediaId: Guid.NewGuid(),
            StartSeconds: start,
            EndSeconds: end,
            SourceText: sourceText,
            Ipa: sourceText,
            Phonemes: [sourceText],
            Score: score,
            QueryPhonemes: [sourceText]);

        return new SearchResultRowViewModel(
            result, new FakeMediaPlayerLauncher(), new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService())
        {
            MediaPath = mediaPath,
        };
    }

    private static ResultGroupViewModel Group(string label, params SearchResultRowViewModel[] members) => new(label, members);

    private static AssemblyDraftViewModel MakeDraft(params ResultGroupViewModel[] groups) =>
        new(groups, new FakeMediaPlayerLauncher(), new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService());

    [Fact]
    public void Constructor_OneSlotPerGroup_DefaultingToTheTopRankedCandidate()
    {
        // Candidates arrive already sorted by ResultSortFilter - the draft trusts that order.
        var best = Row("maken", score: 0.9);
        var worse = Row("laten", score: 0.5);
        var draft = MakeDraft(Group("Covers \"m u\"", best, worse));

        var slot = Assert.Single(draft.Slots);
        Assert.Equal(best, slot.SelectedCandidate);
        Assert.Equal(2, slot.Candidates.Count);
    }

    [Fact]
    public void IsComplete_TracksWhetherAtLeastOneSlotHasAChosenCandidate()
    {
        var draft = MakeDraft(
            Group("A", Row("a", 0.9)),
            Group("B", Row("b", 0.9)));

        Assert.True(draft.IsComplete); // both slots default to their (only) candidate

        draft.Slots[1].SelectedCandidate = null;

        // Skipping a slot's choice doesn't make the draft incomplete - it means "leave this span
        // out of the assembly", which is a valid choice, not a missing requirement.
        Assert.True(draft.IsComplete);

        draft.Slots[0].SelectedCandidate = null;

        Assert.False(draft.IsComplete);
    }

    [Fact]
    public async Task AuditionAsync_WithOneSlotSkipped_StillAssemblesFromTheChosenOnes()
    {
        var draft = MakeDraft(
            Group("A", Row("a", 0.9, mediaPath: "/media/a.mp4", start: 1.0, end: 2.0)),
            Group("B", Row("b", 0.9)));

        draft.Slots[1].SelectedCandidate = null;

        await draft.AuditionCommand.ExecuteAsync(null);

        // Not "pick at least one slot" - slot A was chosen and playable, so extraction should have
        // been attempted (and, absent a real second media file, fails at the ffmpeg step rather
        // than at the "nothing to render" gate this test is actually checking).
        Assert.DoesNotContain("Pick at least one slot", draft.Status);
    }

    [Fact]
    public void IsComplete_EmptyDraft_IsNotComplete()
    {
        var draft = MakeDraft();

        Assert.False(draft.IsComplete);
    }

    [Fact]
    public void SlotSelectionChange_UpdatesAuditionAndExportCanExecute()
    {
        var draft = MakeDraft(Group("A", Row("a", 0.9)));

        Assert.True(draft.AuditionCommand.CanExecute(null));
        Assert.True(draft.ExportCommand.CanExecute(null));

        draft.Slots[0].SelectedCandidate = null;

        Assert.False(draft.AuditionCommand.CanExecute(null));
        Assert.False(draft.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task AuditionAsync_WithAnUnplayableCandidate_ReportsWithoutAttemptingExtraction()
    {
        // Complete (every slot has a selection) but not playable - no MediaPath at all.
        var draft = MakeDraft(Group("A", Row("a", 0.9, mediaPath: null)));

        await draft.AuditionCommand.ExecuteAsync(null);

        Assert.Contains("timed, playable clip", draft.Status);
    }

    [Fact]
    public async Task ExportAsync_WithAnUntimedCandidate_ReportsWithoutPromptingForAPath()
    {
        var picker = new FakeFilePickerService();
        var draft = new AssemblyDraftViewModel(
            [Group("A", Row("a", 0.9, mediaPath: "/media/a.mp4", start: null, end: null))],
            new FakeMediaPlayerLauncher(), new FFmpegClipExtractor(new FFmpegToolLocator()), picker);

        await draft.ExportCommand.ExecuteAsync(null);

        Assert.Contains("timed, playable clip", draft.Status);
        Assert.Null(picker.LastSuggestedFileName);
    }
}
