using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// A result from a transcript that never had timing must not be presented as one at 00:00 (#32).
/// The old representation was 0/0 plus a comment asking every layer to reinterpret it; nothing
/// did, so play, clip export and copy-timestamp all silently acted on a stand-in zero.
/// </summary>
public class UntimedResultTests
{
    private static SearchResultRowViewModel Row(double? start, double? end) =>
        new(
            new SearchResult(Guid.NewGuid(), start, end, "een", "eːn", ["eː", "n"], ["eː", "n"], 0.9),
            new FakeMediaPlayerLauncher(),
            new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()),
            new FakeFilePickerService())
        { MediaPath = "/tmp/clip.mp3" };

    [Fact]
    public void AnUntimedResultSaysSoInsteadOfShowingZero()
    {
        Assert.Equal("no timing", Row(null, null).TimeRangeDisplay);
    }

    [Fact]
    public void ARealZeroSecondResultStillShowsATimestamp()
    {
        // The distinction the sentinel destroyed: a match genuinely at the start of a file.
        var row = Row(0, 0.4);

        Assert.NotEqual("no timing", row.TimeRangeDisplay);
        Assert.True(row.HasTiming);
    }

    [Fact]
    public void PlayAndExportAreUnavailableWithoutTiming()
    {
        var row = Row(null, null);

        Assert.False(row.PlayCommand.CanExecute(null));
        Assert.False(row.ExportClipCommand.CanExecute(null));
        Assert.False(row.CopyTimestampCommand.CanExecute(null));
    }

    [Fact]
    public void PlayAndExportAreAvailableWithTimingAndMedia()
    {
        var row = Row(1.0, 2.0);

        Assert.True(row.PlayCommand.CanExecute(null));
        Assert.True(row.ExportClipCommand.CanExecute(null));
        Assert.True(row.CopyTimestampCommand.CanExecute(null));
    }

    [Fact]
    public void TextCopyActionsStillWorkWithoutTiming()
    {
        // An untimed result is still useful for finding what was said - only the
        // timing-dependent actions are gone.
        var row = Row(null, null);

        Assert.True(row.CopyTextCommand.CanExecute(null));
        Assert.True(row.CopyIpaCommand.CanExecute(null));
        Assert.True(row.CopyPhonemesCommand.CanExecute(null));
    }
}
