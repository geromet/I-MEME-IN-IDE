using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

public class SearchResultRowViewModelTests
{
    private static SearchResult MakeResult() => new(
        MediaId: Guid.NewGuid(),
        StartSeconds: 12.5,
        EndSeconds: 14.0,
        SourceText: "a long bus",
        Ipa: "ə lɔŋ bʌs",
        MatchPhonemes: ["ə", "l", "ɔ", "ŋ", "b", "ʌ", "s"],
        QueryPhonemes: ["ɐ", "m", "ʌ", "ŋ", "ʌ", "s"],
        Score: 0.87);

    private static FFmpegClipExtractor MakeClipExtractor() => new(new FFmpegToolLocator());

    [Fact]
    public void PlayCommand_CannotExecuteBeforeMediaPathIsResolved()
    {
        var launcher = new FakeMediaPlayerLauncher();
        var row = new SearchResultRowViewModel(MakeResult(), launcher, new FakeClipboardService(), MakeClipExtractor(), new FakeFilePickerService());

        Assert.False(row.PlayCommand.CanExecute(null));

        row.MediaPath = "/media/clip.mp4";
        Assert.True(row.PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task PlayCommand_OpensTheResolvedPathAtTheResultsStartTime()
    {
        var launcher = new FakeMediaPlayerLauncher();
        var row = new SearchResultRowViewModel(MakeResult(), launcher, new FakeClipboardService(), MakeClipExtractor(), new FakeFilePickerService())
        {
            MediaPath = "/media/clip.mp4",
        };

        await row.PlayCommand.ExecuteAsync(null);

        Assert.Equal("/media/clip.mp4", launcher.LastMediaPath);
        Assert.Equal(12.5, launcher.LastStartSeconds);
        Assert.Equal(1, launcher.CallCount);
    }

    [Fact]
    public async Task PlayCommand_ReportsWhenNoSeekCapablePlayerWasFound()
    {
        var launcher = new FakeMediaPlayerLauncher { Result = new(true, false, null) };
        var row = new SearchResultRowViewModel(MakeResult(), launcher, new FakeClipboardService(), MakeClipExtractor(), new FakeFilePickerService())
        {
            MediaPath = "/media/clip.mp4",
        };

        await row.PlayCommand.ExecuteAsync(null);

        Assert.Contains("no seek-capable player", row.PlaybackStatus);
    }

    [Fact]
    public async Task PlayCommand_ReportsFailureFromTheLauncher()
    {
        var launcher = new FakeMediaPlayerLauncher { Result = new(false, false, "boom") };
        var row = new SearchResultRowViewModel(MakeResult(), launcher, new FakeClipboardService(), MakeClipExtractor(), new FakeFilePickerService())
        {
            MediaPath = "/media/clip.mp4",
        };

        await row.PlayCommand.ExecuteAsync(null);

        Assert.Contains("boom", row.PlaybackStatus);
    }

    [Fact]
    public async Task CopyCommands_SendTheExpectedTextToTheClipboard()
    {
        var clipboard = new FakeClipboardService();
        var row = new SearchResultRowViewModel(MakeResult(), new FakeMediaPlayerLauncher(), clipboard, MakeClipExtractor(), new FakeFilePickerService());

        await row.CopyTextCommand.ExecuteAsync(null);
        await row.CopyIpaCommand.ExecuteAsync(null);
        await row.CopyPhonemesCommand.ExecuteAsync(null);
        await row.CopyTimestampCommand.ExecuteAsync(null);

        Assert.Equal(
            ["a long bus", "ə lɔŋ bʌs", "ə l ɔ ŋ b ʌ s", "00:00:12.50"],
            clipboard.CopiedTexts);
    }

    [Fact]
    public void ExportClipCommand_CannotExecuteBeforeMediaPathIsResolved()
    {
        var row = new SearchResultRowViewModel(MakeResult(), new FakeMediaPlayerLauncher(), new FakeClipboardService(), MakeClipExtractor(), new FakeFilePickerService());

        Assert.False(row.ExportClipCommand.CanExecute(null));

        row.MediaPath = "/media/clip.mp4";
        Assert.True(row.ExportClipCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportClipCommand_DoesNothingWhenTheUserCancelsTheSaveDialog()
    {
        var filePicker = new FakeFilePickerService { ClipExportPathToReturn = null };
        var row = new SearchResultRowViewModel(MakeResult(), new FakeMediaPlayerLauncher(), new FakeClipboardService(), MakeClipExtractor(), filePicker)
        {
            MediaPath = "/media/clip.mp4",
        };

        await row.ExportClipCommand.ExecuteAsync(null);

        Assert.Equal("", row.PlaybackStatus);
    }

    [Fact]
    public async Task ExportClipCommand_SuggestsAFileNameDerivedFromTheSourceText()
    {
        var filePicker = new FakeFilePickerService { ClipExportPathToReturn = null };
        var row = new SearchResultRowViewModel(MakeResult(), new FakeMediaPlayerLauncher(), new FakeClipboardService(), MakeClipExtractor(), filePicker)
        {
            MediaPath = "/media/clip.mp4",
        };

        await row.ExportClipCommand.ExecuteAsync(null);

        Assert.Equal("a_long_bus.mp4", filePicker.LastSuggestedFileName);
    }
}
