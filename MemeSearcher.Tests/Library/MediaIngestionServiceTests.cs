using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Tests.Library;

public class MediaIngestionServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir;

    public MediaIngestionServiceTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("memesearcher-test-").FullName;
    }

    private MemeSearcherDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MemeSearcherDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        var context = new MemeSearcherDbContext(options);
        context.Database.Migrate();
        return context;
    }

    /// <summary>
    /// These tests exercise the real espeak-ng binary rather than a fake IPhonemizer, matching
    /// EspeakPhonemizerTests' rationale: the risk lives in the process boundary. Returns null
    /// (caller skips) if espeak-ng isn't installed on the machine running the tests.
    /// </summary>
    private static async Task<IPhonemizer?> CreatePhonemizerIfAvailableAsync()
    {
        var locator = new EspeakToolLocator();
        var status = await locator.LocateAsync();
        return status.IsInstalled ? new EspeakPhonemizer(locator) : null;
    }

    [Fact]
    public async Task ImportAsync_ParsesTranscriptAndBuildsSegmentsWordsInDatabase()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            among us

            """);

        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var service = new MediaIngestionService(context, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));

        var result = await service.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));

        Assert.Equal(MediaIngestionOutcome.Imported, result.Outcome);

        var storedTranscript = await context.Transcripts
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .SingleAsync(t => t.MediaId == result.Media.Id);

        var segment = Assert.Single(storedTranscript.Segments);
        Assert.Equal("among us", segment.Text);
        Assert.Equal(2, segment.Words.Count);
        Assert.Equal("among", segment.Words[0].Text);
        Assert.Equal("us", segment.Words[1].Text);
        // Word timing should stay within the segment's cue bounds.
        Assert.InRange(segment.Words[0].StartSeconds, segment.StartSeconds, segment.EndSeconds);
        Assert.Equal(segment.EndSeconds, segment.Words[^1].EndSeconds, 3);

        // Phonemization results should now be persisted on both the segment and its words.
        Assert.False(string.IsNullOrWhiteSpace(segment.Ipa));
        Assert.False(string.IsNullOrWhiteSpace(segment.PhonemeSequence));
        Assert.Contains(" | ", segment.PhonemeSequence);
        Assert.False(string.IsNullOrWhiteSpace(segment.Words[0].Ipa));
        Assert.False(string.IsNullOrWhiteSpace(segment.Words[0].PhonemeSequence));
        Assert.DoesNotContain('_', segment.Words[0].PhonemeSequence!);
    }

    [Fact]
    public async Task ImportAsync_ReimportingSameContentHashDoesNotReprocess()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            hello

            """);

        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var service = new MediaIngestionService(context, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));

        var first = await service.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));
        var second = await service.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));

        Assert.Equal(MediaIngestionOutcome.Imported, first.Outcome);
        Assert.Equal(MediaIngestionOutcome.AlreadyIndexed, second.Outcome);
        Assert.Equal(first.Media.Id, second.Media.Id);

        Assert.Equal(1, await context.Media.CountAsync());
        Assert.Equal(1, await context.Transcripts.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_MediaWithNoTranscriptFile_TranscribesAndBuildsSegmentsWords()
    {
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");

        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        // whisperx isn't installed here, so a fake ITranscriptionProvider stands in - this test's
        // job is to prove the ingestion pipeline correctly converts a TranscriptionResult into
        // the same Segment/Word/phoneme data an SRT import produces, not to test whisperx itself
        // (see WhisperXTranscriptionProviderTests for that boundary).
        var fakeTranscription = new FakeTranscriptionProvider([
            new(0.5, 2.0, "a long bus"),
        ]);

        await using var context = CreateContext();
        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer, fakeTranscription, new MediaMetadataProbe(new FFprobeToolLocator()));

        var result = await service.ImportAsync(new MediaIngestionRequest(mediaPath, null, "en-US"));

        Assert.Equal(MediaIngestionOutcome.Imported, result.Outcome);
        Assert.Equal(mediaPath, fakeTranscription.LastMediaPath);
        Assert.Equal("en-US", fakeTranscription.LastLanguage);

        var storedTranscript = await context.Transcripts
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .SingleAsync(t => t.MediaId == result.Media.Id);

        Assert.Equal("fake-transcriber", storedTranscript.Source);

        var segment = Assert.Single(storedTranscript.Segments);
        Assert.Equal("a long bus", segment.Text);
        Assert.Equal(0.5, segment.StartSeconds);
        Assert.Equal(2.0, segment.EndSeconds);
        Assert.Equal(3, segment.Words.Count);
        Assert.False(string.IsNullOrWhiteSpace(segment.PhonemeSequence));
    }

    [Fact]
    public async Task ImportAsync_NeitherMediaPathNorTranscriptPathThrows()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportAsync(new MediaIngestionRequest(null, null, "en-US")));
    }

    public void Dispose()
    {
        SqliteConnectionCleanup.TryDeleteFile(_dbPath);
        Directory.Delete(_tempDir, recursive: true);
    }
}

internal static class SqliteConnectionCleanup
{
    public static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; SQLite may still hold the file briefly after disposal.
        }
    }
}
