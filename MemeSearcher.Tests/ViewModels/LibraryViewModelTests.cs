using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.Services;
using MemeSearcher.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// Exercises LibraryViewModel against real services (real espeak-ng, a real temp-file SQLite db)
/// with only the file picker stubbed. Skips (returns early) if espeak-ng isn't installed.
/// </summary>
public class LibraryViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-libraryvm-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-libraryvm-test-").FullName;

    private class StubFilePickerService(params IReadOnlyList<string> paths) : IFilePickerService
    {
        public Task<IReadOnlyList<string>> PickMediaFilesAsync() => Task.FromResult(paths);
    }

    private async Task<LibraryViewModel?> TrySetUpAsync(params IReadOnlyList<string> pickedFilePaths)
    {
        var locator = new EspeakToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return null;
        }

        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await dbContextFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var phonemizer = new EspeakPhonemizer(locator);
        var ingestion = new MediaIngestionService(await dbContextFactory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        var libraryService = new LibraryService(dbContextFactory);

        return new LibraryViewModel(libraryService, ingestion, new StubFilePickerService(pickedFilePaths));
    }

    [Fact]
    public async Task ImportAsync_TranscriptOnly_AddsAnItemMarkedAsNotPlayable()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var viewModel = await TrySetUpAsync(srtPath);
        if (viewModel is null)
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.Items);
        Assert.Equal("clip.srt", item.Title);
        Assert.Equal("en-US", item.Language);
        Assert.Contains("1 segment", item.SegmentsDisplay);
        Assert.Contains("2/2 phonemized", item.PhonemeCoverageDisplay);

        // The bug this fixes: a transcript-only import must not be reported as playable media.
        Assert.Equal("transcript only", item.PlayableMediaDisplay);
    }

    [Fact]
    public async Task ImportAsync_TranscriptAndMediaFile_AddsAnItemMarkedAsPlayable()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");

        var viewModel = await TrySetUpAsync(srtPath, mediaPath);
        if (viewModel is null)
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.Items);
        Assert.Equal("🎬 playable", item.PlayableMediaDisplay);
    }

    [Fact]
    public async Task ImportAsync_MediaFileWithNoTranscript_AttemptsTranscriptionInsteadOfErroring()
    {
        // Milestone 3: a bare media file is a valid selection now - it should reach
        // ITranscriptionProvider rather than being rejected by file-selection validation.
        // TrySetUpAsync wires UnusedTranscriptionProvider, which throws a distinctive message,
        // so seeing that message (not a "select a transcript" validation error) proves the
        // classification logic let it through.
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video");

        var viewModel = await TrySetUpAsync(mediaPath);
        if (viewModel is null)
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Items);
        Assert.Contains("should not need transcription", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ImportAsync_TwoMediaFilesReportsAnError()
    {
        var mediaPathA = Path.Combine(_tempDir, "a.mp4");
        var mediaPathB = Path.Combine(_tempDir, "b.mp4");
        await File.WriteAllTextAsync(mediaPathA, "not a real video");
        await File.WriteAllTextAsync(mediaPathB, "not a real video");

        var viewModel = await TrySetUpAsync(mediaPathA, mediaPathB);
        if (viewModel is null)
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Items);
        Assert.Contains("Select only one audio/video file", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadAsync_WithNoImportedMedia_ReportsEmptyLibrary()
    {
        var viewModel = await TrySetUpAsync();
        if (viewModel is null)
        {
            return;
        }

        await viewModel.LoadAsync();

        Assert.Empty(viewModel.Items);
        Assert.Equal("No media imported yet.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RemoveFromLibraryAsync_DeletesTheDbRowButKeepsTheSourceFile()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var viewModel = await TrySetUpAsync(srtPath);
        if (viewModel is null)
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.Items);

        await viewModel.RemoveFromLibraryCommand.ExecuteAsync(item);

        Assert.Empty(viewModel.Items);
        Assert.True(File.Exists(srtPath)); // addendum §29: removing from library must not touch the file
    }

    [Fact]
    public async Task DeleteSourceFileAsync_RequiresTwoClicksBeforeActuallyDeleting()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var viewModel = await TrySetUpAsync(srtPath);
        if (viewModel is null)
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.Items);

        // First click arms the confirmation - nothing should be deleted yet.
        await viewModel.DeleteSourceFileCommand.ExecuteAsync(item);
        Assert.True(item.IsPendingDelete);
        Assert.Single(viewModel.Items);
        Assert.True(File.Exists(srtPath));

        // Second click on the now-armed row actually deletes.
        await viewModel.DeleteSourceFileCommand.ExecuteAsync(item);
        Assert.Empty(viewModel.Items);
        Assert.False(File.Exists(srtPath));
    }

    [Fact]
    public async Task CancelDelete_DisarmsTheConfirmation()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var viewModel = await TrySetUpAsync(srtPath);
        if (viewModel is null)
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.Items);

        await viewModel.DeleteSourceFileCommand.ExecuteAsync(item);
        Assert.True(item.IsPendingDelete);

        viewModel.CancelDeleteCommand.Execute(item);
        Assert.False(item.IsPendingDelete);
        Assert.True(File.Exists(srtPath));
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        Directory.Delete(_tempDir, recursive: true);
    }
}
