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
        Assert.InRange(segment.Words[0].StartSeconds!.Value, segment.StartSeconds!.Value, segment.EndSeconds!.Value);
        Assert.Equal(segment.EndSeconds!.Value, segment.Words[^1].EndSeconds!.Value, 3);

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
    public async Task ImportAsync_WithRealWordTimingFromTheProvider_UsesItInsteadOfInterpolating()
    {
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");

        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        // Deliberately uneven real timing that the character-proportional interpolation would
        // never produce on its own - proves the real values are actually used, not recomputed.
        var fakeTranscription = new FakeTranscriptionProvider([
            new(0.5, 2.0, "a long bus", [
                new("a", 0.5, 0.6),
                new("long", 0.6, 1.9),
                new("bus", 1.9, 2.0),
            ]),
        ]);

        await using var context = CreateContext();
        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer, fakeTranscription, new MediaMetadataProbe(new FFprobeToolLocator()));

        var result = await service.ImportAsync(new MediaIngestionRequest(mediaPath, null, "en-US"));

        var storedTranscript = await context.Transcripts
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .SingleAsync(t => t.MediaId == result.Media.Id);

        var words = Assert.Single(storedTranscript.Segments).Words;
        Assert.Equal(0.5, words[0].StartSeconds);
        Assert.Equal(0.6, words[0].EndSeconds);
        Assert.Equal(0.6, words[1].StartSeconds);
        Assert.Equal(1.9, words[1].EndSeconds);
        Assert.Equal(1.9, words[2].StartSeconds);
        Assert.Equal(2.0, words[2].EndSeconds);
    }

    [Fact]
    public async Task ImportAsync_WhenRealWordCountDoesNotMatchPhonemizedCount_FallsBackToInterpolation()
    {
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");

        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        // Only 2 "real" words given for a 3-word phrase - a mismatch that must not be guessed at.
        var fakeTranscription = new FakeTranscriptionProvider([
            new(0.5, 2.0, "a long bus", [
                new("a long", 0.5, 1.0),
                new("bus", 1.9, 2.0),
            ]),
        ]);

        await using var context = CreateContext();
        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer, fakeTranscription, new MediaMetadataProbe(new FFprobeToolLocator()));

        var result = await service.ImportAsync(new MediaIngestionRequest(mediaPath, null, "en-US"));

        var storedTranscript = await context.Transcripts
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .SingleAsync(t => t.MediaId == result.Media.Id);

        var words = Assert.Single(storedTranscript.Segments).Words;
        Assert.Equal(3, words.Count); // phonemizer's own split, not the mismatched "real" word count
        Assert.Equal(0.5, words[0].StartSeconds); // interpolation still starts/ends at the cue bounds
        Assert.Equal(2.0, words[^1].EndSeconds);
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

    [Fact]
    public async Task RealignAsync_UpdatesWordTimingAndPopulatesPhonesFromTheAlignmentProvider()
    {
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            hello world

            """);

        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        // Real per-word and per-phone timing an MFA-style provider would produce - deliberately
        // uneven, so it's obvious this came from the provider and not interpolation.
        var fakeAlignment = new FakeAlignmentProvider(new AlignmentResult(
            [
                new("hello", 1.0, 1.7),
                new("world", 1.7, 3.0),
            ],
            [
                new("HH", 1.0, 1.2), new("AH0", 1.2, 1.4), new("L", 1.4, 1.55), new("OW1", 1.55, 1.7),
                new("W", 1.7, 1.9), new("ER1", 1.9, 2.4), new("L", 2.4, 2.7), new("D", 2.7, 3.0),
            ]));

// A fresh DbContext per operation, matching how MediaIngestionService is actually used in
        // production (a new scoped context per unit of work via DI) - reusing one context's
        // change tracker across Import then Realign is a test-only shortcut that doesn't reflect
        // real usage.
        await using (var importContext = CreateContext())
        {
            var importService = new MediaIngestionService(
                importContext, TranscriptParserFactory.CreateDefault(), phonemizer,
                new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
            await importService.ImportAsync(new MediaIngestionRequest(mediaPath, srtPath, "en-US"));
        }

        await using var context = CreateContext();
        var imported = await context.Media.SingleAsync();

        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()), fakeAlignment);

        var result = await service.RealignAsync(imported.Id);

        Assert.Equal(2, result.UpdatedWordCount);
        Assert.Equal(8, result.UpdatedPhoneCount);
        Assert.Equal(mediaPath, fakeAlignment.LastMediaPath);
        Assert.Equal("hello world", fakeAlignment.LastTranscriptText);

        var storedTranscript = await context.Transcripts
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .ThenInclude(w => w.Phones)
            .SingleAsync(t => t.MediaId == imported.Id);

        var words = storedTranscript.Segments.Single().Words.OrderBy(w => w.Sequence).ToList();
        Assert.Equal(1.0, words[0].StartSeconds);
        Assert.Equal(1.7, words[0].EndSeconds);
        Assert.Equal(1.7, words[1].StartSeconds);
        Assert.Equal(3.0, words[1].EndSeconds);

        var helloPhones = words[0].Phones.OrderBy(p => p.Sequence).Select(p => p.Symbol).ToList();
        Assert.Equal(["HH", "AH0", "L", "OW1"], helloPhones);
    }

    [Fact]
    public async Task RealignAsync_WithNoAlignmentProviderConfiguredThrows()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        await using var context = CreateContext();
        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RealignAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RealignAsync_OnATranscriptOnlyImportThrows()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            hello world

            """);

        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        var fakeAlignment = new FakeAlignmentProvider(new AlignmentResult([]));

        await using var context = CreateContext();
        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()), fakeAlignment);

        var imported = await service.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RealignAsync(imported.Media.Id));
    }

    /// <summary>
    /// The phonemizer half of the declaration check (#18). A phonemizer whose declared alphabet
    /// contradicts what it actually emits must fail the import rather than write mis-tagged
    /// phonemes that later convert wrongly and silently stop matching.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RefusesAPhonemizerWhoseDeclaredAlphabetContradictsItsOutput()
    {
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            hello world

            """);

        await using var context = CreateContext();
        var service = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(),
            new MisdeclaringPhonemizer(), new UnusedTranscriptionProvider(),
            new MediaMetadataProbe(new FFprobeToolLocator()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US")));

        Assert.Contains("declares Ipa", ex.Message);
        Assert.Contains("Arpabet", ex.Message);
    }

    /// <summary>Claims IPA, emits unmistakable ARPABET.</summary>
    private sealed class MisdeclaringPhonemizer : IPhonemizer
    {
        public string ProviderName => "misdeclaring";

        public IReadOnlyCollection<string> SupportedLanguages => ["en-US"];

        public Core.Phonetics.PhoneAlphabet Alphabet => Core.Phonetics.PhoneAlphabet.Ipa;

        public Task<PhonemizationResult> PhonemizeAsync(string text, string language, CancellationToken cancellationToken = default)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => new PhonemizedWord(w, "x", new[] { "HH", "AH0", "L", "OW1" }))
                .ToList();

            return Task.FromResult(new PhonemizationResult(text, "x", words));
        }
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
