using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Infrastructure.YtDlp;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Tests.YtDlp;

/// <summary>
/// Real espeak-ng and a real SQLite database (same rationale as MediaIngestionServiceTests), but
/// the download step is a canned delegate rather than a real yt-dlp invocation - ImportAsync's
/// delegate overload exists specifically so this per-item success/failure/patch logic is testable
/// without a real download (#27).
/// </summary>
public class YtDlpImportOrchestratorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-orchestrator-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("ytdlp-orchestrator-test-").FullName;

    private MemeSearcherDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MemeSearcherDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var context = new MemeSearcherDbContext(options);
        context.Database.Migrate();
        return context;
    }

    private static async Task<IPhonemizer?> CreatePhonemizerIfAvailableAsync()
    {
        var locator = new EspeakToolLocator();
        var status = await locator.LocateAsync();
        return status.IsInstalled ? new EspeakPhonemizer(locator) : null;
    }

    private string WriteFakeMediaFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "not a real media file, just needs to exist");
        return path;
    }

    private YtDlpImportOrchestrator CreateOrchestrator(MemeSearcherDbContext context, IPhonemizer phonemizer)
    {
        var transcription = new FakeTranscriptionProvider([
            new TranscribedSegment(0, 1, "hello world", null),
        ]);
        var ingestion = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer, transcription,
            new MediaMetadataProbe(new FFprobeToolLocator()));

        // Never actually invoked (ImportAsync's delegate overload replaces this) - only present
        // because the orchestrator's constructor takes the real provider for production use.
        var unusedDownloadProvider = new YtDlpDownloadProvider(
            new YtDlpToolLocator(), new YtDlpSettings(), new InMemorySettingsStore());

        return new YtDlpImportOrchestrator(unusedDownloadProvider, ingestion, context);
    }

    private static YtDlpImportPlan PlanOf(params YtDlpVideoEntry[] entries) =>
        new(entries.Select(e => new YtDlpImportPlanItem(e, YtDlpImportPlanStatus.New)).ToList());

    [Fact]
    public async Task ImportAsync_DownloadsAndImportsEveryNewItemAndStampsYtDlpMetadata()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context, phonemizer);

        var entry = new YtDlpVideoEntry("vid1", "A Test Video", "Some Channel", "https://www.youtube.com/watch?v=vid1");
        var plan = PlanOf(entry);

        Task<YtDlpDownloadResult> Download(string url, CancellationToken ct) =>
            Task.FromResult(new YtDlpDownloadResult(
                WriteFakeMediaFile("vid1.mp3"), "vid1", "A Test Video", "Some Channel",
                new DateOnly(2024, 1, 2), YtDlpMediaKind.Audio));

        var messages = new List<string>();
        var summary = await orchestrator.ImportAsync(plan, "en-US", Download, new Progress<string>(messages.Add));

        Assert.Equal(1, summary.Imported);
        Assert.Equal(0, summary.Failed);

        var media = await context.Media.SingleAsync();
        Assert.Equal("vid1", media.VideoId);
        Assert.Equal("Some Channel", media.Channel);
        Assert.Equal(new DateOnly(2024, 1, 2), media.UploadDate);
        Assert.Equal(YtDlpMediaKind.Audio, media.YtDlpMediaKind);
        Assert.Contains(messages, m => m.Contains("Done: 1 imported, 0 failed"));
    }

    [Fact]
    public async Task ImportAsync_ADownloadFailureIsRecordedAndDoesNotAbortTheBatch()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context, phonemizer);

        var failing = new YtDlpVideoEntry("bad1", "A Private Video", null, "https://www.youtube.com/watch?v=bad1");
        var good = new YtDlpVideoEntry("good1", "A Fine Video", null, "https://www.youtube.com/watch?v=good1");
        var plan = PlanOf(failing, good);

        Task<YtDlpDownloadResult> Download(string url, CancellationToken ct) =>
            url.Contains("bad1")
                ? throw new InvalidOperationException("yt-dlp exited with code 1: ERROR: Private video")
                : Task.FromResult(new YtDlpDownloadResult(
                    WriteFakeMediaFile("good1.mp3"), "good1", "A Fine Video", null, null, YtDlpMediaKind.Audio));

        var summary = await orchestrator.ImportAsync(plan, "en-US", Download, new Progress<string>());

        Assert.Equal(1, summary.Imported);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, await context.Media.CountAsync());

        var failure = await context.YtDlpImportFailures.SingleAsync();
        Assert.Equal("bad1", failure.VideoId);
        Assert.Contains("Private video", failure.Reason);
        Assert.Equal(1, failure.AttemptCount);
    }

    [Fact]
    public async Task ImportAsync_RepeatedFailureOfTheSameVideoIncrementsAttemptCountRatherThanDuplicating()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context, phonemizer);

        var entry = new YtDlpVideoEntry("bad1", "A Private Video", null, "https://www.youtube.com/watch?v=bad1");
        var plan = PlanOf(entry);

        Task<YtDlpDownloadResult> Download(string url, CancellationToken ct) =>
            throw new InvalidOperationException("still private");

        await orchestrator.ImportAsync(plan, "en-US", Download, new Progress<string>());
        await orchestrator.ImportAsync(plan, "en-US", Download, new Progress<string>());

        Assert.Equal(1, await context.YtDlpImportFailures.CountAsync());
        var failure = await context.YtDlpImportFailures.SingleAsync();
        Assert.Equal(2, failure.AttemptCount);
    }

    [Fact]
    public async Task ImportAsync_ContentAlreadyBelongingToADifferentVideoIdIsRecordedAsAFailureNotOverwritten()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context, phonemizer);

        // Same file content both times (same content hash) - MediaIngestionService will dedup the
        // second import against the first, but the first already has a *different* VideoId.
        var sharedContent = WriteFakeMediaFile("shared.mp3");

        var firstEntry = new YtDlpVideoEntry("videoA", "First", null, "https://www.youtube.com/watch?v=videoA");
        await orchestrator.ImportAsync(
            PlanOf(firstEntry), "en-US",
            (_, _) => Task.FromResult(new YtDlpDownloadResult(sharedContent, "videoA", "First", null, null, YtDlpMediaKind.Audio)),
            new Progress<string>());

        var secondEntry = new YtDlpVideoEntry("videoB", "Second", null, "https://www.youtube.com/watch?v=videoB");
        var summary = await orchestrator.ImportAsync(
            PlanOf(secondEntry), "en-US",
            (_, _) => Task.FromResult(new YtDlpDownloadResult(sharedContent, "videoB", "Second", null, null, YtDlpMediaKind.Audio)),
            new Progress<string>());

        Assert.Equal(0, summary.Imported);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, await context.Media.CountAsync());
        Assert.Equal("videoA", (await context.Media.SingleAsync()).VideoId);

        var failure = await context.YtDlpImportFailures.SingleAsync();
        Assert.Equal("videoB", failure.VideoId);
        Assert.Contains("videoA", failure.Reason);
    }

    [Fact]
    public async Task ImportAsync_EmptyPlan_ImportsNothing()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context, phonemizer);

        var summary = await orchestrator.ImportAsync(
            PlanOf(), "en-US", (_, _) => throw new InvalidOperationException("should never be called"), new Progress<string>());

        Assert.Equal(0, summary.Total);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        Directory.Delete(_tempDir, recursive: true);
    }
}
