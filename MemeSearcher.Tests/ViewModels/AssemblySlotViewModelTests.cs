using System;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

public class AssemblySlotViewModelTests
{
    private static SearchResultRowViewModel Row(string sourceText)
    {
        var result = new SearchResult(
            MediaId: Guid.NewGuid(),
            StartSeconds: null,
            EndSeconds: null,
            SourceText: sourceText,
            Ipa: sourceText,
            Phonemes: [sourceText],
            Score: 0.8,
            QueryPhonemes: [sourceText]);

        return new SearchResultRowViewModel(
            result, new FakeMediaPlayerLauncher(), new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService());
    }

    [Fact]
    public void Constructor_DefaultsToTheFirstCandidate()
    {
        var first = Row("a");
        var slot = new AssemblySlotViewModel("Covers \"x\"", 0, 1, [first, Row("b")]);

        Assert.Equal(first, slot.SelectedCandidate);
    }

    /// <summary>Skipping a span is a deliberate choice (#25 review) - not an error state, so it must be reachable without picking through the ComboBox for a "none" entry that doesn't exist.</summary>
    [Fact]
    public void SkipCommand_ClearsTheSelectedCandidate()
    {
        var slot = new AssemblySlotViewModel("Covers \"x\"", 0, 1, [Row("a")]);

        slot.SkipCommand.Execute(null);

        Assert.Null(slot.SelectedCandidate);
    }
}
