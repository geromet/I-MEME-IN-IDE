using MemeSearcher.Core.Jobs;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Jobs;
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
///
/// Milestone 14: Import/Realign/Reindex are queued jobs now (#14) - ImportCommand.ExecuteAsync
/// returns as soon as the job is enqueued, not once it has run. Tests that need the import to have
/// actually landed poll the real IJobQueue the view model was built with until its job reaches a
/// terminal state, the same way MainWindowViewModelTests already polls for its own async fan-out.
/// </summary>
public class LibraryViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-libraryvm-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-libraryvm-test-").FullName;

    private class StubFilePickerService(params IReadOnlyList<string> paths) : IFilePickerService
    {
        public Task<IReadOnlyList<string>> PickMediaFilesAsync() => Task.FromResult(paths);

        public Task<string?> PickClipExportPathAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    }

    private async Task<(LibraryViewModel ViewModel, IJobQueue JobQueue)?> TrySetUpAsync(params IReadOnlyList<string> pickedFilePaths)
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
        var jobQueue = new JobQueueService();

        var viewModel = new LibraryViewModel(
            libraryService, ingestion, new StubFilePickerService(pickedFilePaths), new InMemorySettingsStore(),
            new Infrastructure.Search.PhoneNGramIndexService(dbContextFactory), jobQueue);

        return (viewModel, jobQueue);
    }

    /// <summary>Waits for every job the queue currently knows about to reach a terminal state.</summary>
    private static async Task WaitForJobsToFinishAsync(IJobQueue jobQueue, int timeoutMs = 5000)
    {
        var elapsed = 0;
        while (jobQueue.Jobs.Any(j => j.State is JobState.Queued or JobState.Running) && elapsed < timeoutMs)
        {
            await Task.Delay(10);
            elapsed += 10;
        }
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

        var setup = await TrySetUpAsync(srtPath);
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;
        await viewModel.ImportCommand.ExecuteAsync(null);
        await WaitForJobsToFinishAsync(jobQueue);

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

        var setup = await TrySetUpAsync(srtPath, mediaPath);
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;
        await viewModel.ImportCommand.ExecuteAsync(null);
        await WaitForJobsToFinishAsync(jobQueue);

        var item = Assert.Single(viewModel.Items);
        Assert.Equal("🎬 playable", item.PlayableMediaDisplay);
    }

    [Fact]
    public async Task ImportAsync_MediaFileWithNoTranscript_AttemptsTranscriptionInsteadOfErroring()
    {
        // Milestone 3: a bare media file is a valid selection now - it should reach
        // ITranscriptionProvider rather than being rejected by file-selection validation.
        // TrySetUpAsync wires UnusedTranscriptionProvider, which throws a distinctive message,
        // so seeing that message on the failed job (not a "select a transcript" validation error)
        // proves the classification logic let it through.
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video");

        var setup = await TrySetUpAsync(mediaPath);
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;
        await viewModel.ImportCommand.ExecuteAsync(null);
        await WaitForJobsToFinishAsync(jobQueue);

        Assert.Empty(viewModel.Items);
        var job = Assert.Single(jobQueue.Jobs);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("should not need transcription", job.Error);
    }

    [Fact]
    public async Task ImportAsync_TwoMediaFilesReportsAnError()
    {
        var mediaPathA = Path.Combine(_tempDir, "a.mp4");
        var mediaPathB = Path.Combine(_tempDir, "b.mp4");
        await File.WriteAllTextAsync(mediaPathA, "not a real video");
        await File.WriteAllTextAsync(mediaPathB, "not a real video");

        var setup = await TrySetUpAsync(mediaPathA, mediaPathB);
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;

        // Classification runs synchronously before anything is queued, so this is still reported
        // directly on the view model rather than as a job.
        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Items);
        Assert.Empty(jobQueue.Jobs);
        Assert.Contains("Select only one audio/video file", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadAsync_WithNoImportedMedia_ReportsEmptyLibrary()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        await setup.Value.ViewModel.LoadAsync();

        Assert.Empty(setup.Value.ViewModel.Items);
        Assert.Equal("No media imported yet.", setup.Value.ViewModel.StatusMessage);
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

        var setup = await TrySetUpAsync(srtPath);
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;
        await viewModel.ImportCommand.ExecuteAsync(null);
        await WaitForJobsToFinishAsync(jobQueue);
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

        var setup = await TrySetUpAsync(srtPath);
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;
        await viewModel.ImportCommand.ExecuteAsync(null);
        await WaitForJobsToFinishAsync(jobQueue);
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

        var setup = await TrySetUpAsync(srtPath);
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;
        await viewModel.ImportCommand.ExecuteAsync(null);
        await WaitForJobsToFinishAsync(jobQueue);
        var item = Assert.Single(viewModel.Items);

        await viewModel.DeleteSourceFileCommand.ExecuteAsync(item);
        Assert.True(item.IsPendingDelete);

        viewModel.CancelDeleteCommand.Execute(item);
        Assert.False(item.IsPendingDelete);
        Assert.True(File.Exists(srtPath));
    }

    /// <summary>
    /// Milestone 14: a failed import's error now lives on its Job (surfaced in the Jobs panel)
    /// rather than LibraryViewModel.StatusMessage - the whole point of the queue replacing the
    /// single status line is that a later job's status can't overwrite an earlier one's failure.
    /// </summary>
    [Fact]
    public async Task ImportAsync_AFailedJob_RecordsAReadableError()
    {
        var setup = await TrySetUpAsync("/does/not/exist.srt");
        if (setup is null)
        {
            return;
        }

        var (viewModel, jobQueue) = setup.Value;
        await viewModel.ImportCommand.ExecuteAsync(null);
        await WaitForJobsToFinishAsync(jobQueue);

        var job = Assert.Single(jobQueue.Jobs);
        Assert.Equal(JobState.Failed, job.State);
        Assert.False(string.IsNullOrEmpty(job.Error));
    }

    [Fact]
    public async Task LoadAsync_LeavesRoutineStatusUnmarked()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        await setup.Value.ViewModel.LoadAsync();

        Assert.False(setup.Value.ViewModel.IsStatusError);
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
