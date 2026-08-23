using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Library;

/// <summary>
/// #9: the persistent phoneme n-gram index is derived data that must (a) get built as a normal
/// part of ingestion/realignment when wired in, (b) never go stale, and (c) be exactly
/// reproducible from scratch (addendum §39's layered-rebuildability rule). Real espeak-ng and a
/// real (temp-file) SQLite database throughout, matching this suite's existing convention -
/// returns early if espeak-ng isn't installed.
/// </summary>
public class PhoneNGramIndexServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-ngram-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-ngram-test-").FullName;

    private async Task<(IPhonemizer Phonemizer, IServiceProvider Services)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return null;
        }

        var services = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider();

        await using (var context = await services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>().CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        return (new EspeakPhonemizer(locator), services);
    }

    private static async Task<Guid> ImportAsync(
        IServiceProvider services, IPhonemizer phonemizer, string tempDir, string fileName, string srtBody,
        IPhoneNGramIndexService? indexService = null)
    {
        var path = Path.Combine(tempDir, fileName);
        await File.WriteAllTextAsync(path, srtBody);

        await using var context = await services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>().CreateDbContextAsync();
        var ingestion = new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(),
            new MediaMetadataProbe(new FFprobeToolLocator()), indexService: indexService);
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        return result.Media.Id;
    }

    [Fact]
    public async Task IndexMediaAsync_PopulatesPostingsMatchingTheBuiltStream()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;
        var factory = services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        var mediaId = await ImportAsync(services, phonemizer, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:03,000
            among us is a fun game

            """);

        var indexService = new PhoneNGramIndexService(factory);
        await indexService.IndexMediaAsync(mediaId);

        await using var context = await factory.CreateDbContextAsync();
        var postings = await context.PhoneNGramPostings.Where(p => p.MediaId == mediaId).ToListAsync();

        var transcripts = await context.Transcripts
            .Where(t => t.MediaId == mediaId)
            .Include(t => t.Segments).ThenInclude(s => s.Words).ThenInclude(w => w.Phones)
            .ToListAsync();
        var expectedOccurrences = PhoneNGramIndexer.Extract(
            PhoneStreamBuilder.Build(transcripts).Select(e => e.Token).ToList());

        Assert.NotEmpty(postings);
        Assert.Equal(expectedOccurrences.Count, postings.Count);
        Assert.Equal(
            expectedOccurrences.Select(o => (o.NGram, o.Position)).OrderBy(p => p).ToList(),
            postings.Select(p => (p.NGram, p.StreamPosition)).OrderBy(p => p).ToList());
    }

    [Fact]
    public async Task ImportAsync_WithIndexServiceWiredIn_PopulatesPostingsAutomatically()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;
        var factory = services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();
        var indexService = new PhoneNGramIndexService(factory);

        var mediaId = await ImportAsync(
            services, phonemizer, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:03,000
            among us

            """,
            indexService);

        await using var context = await factory.CreateDbContextAsync();
        var postingCount = await context.PhoneNGramPostings.CountAsync(p => p.MediaId == mediaId);

        Assert.True(postingCount > 0, "expected ImportAsync to populate the index when one is wired in");
    }

    [Fact]
    public async Task ImportAsync_WithoutIndexServiceLeavesNoPostings()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        var mediaId = await ImportAsync(services, phonemizer, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:03,000
            among us

            """);

        await using var context = await services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>().CreateDbContextAsync();
        var postingCount = await context.PhoneNGramPostings.CountAsync(p => p.MediaId == mediaId);

        Assert.Equal(0, postingCount);
    }

    [Fact]
    public async Task RealignAsync_RebuildsTheIndexAndDropsStalePostings()
    {
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");
        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            hello world

            """);

        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;
        var factory = services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();
        var indexService = new PhoneNGramIndexService(factory);

        Guid mediaId;
        await using (var importContext = await factory.CreateDbContextAsync())
        {
            var importService = new MediaIngestionService(
                importContext, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(),
                new MediaMetadataProbe(new FFprobeToolLocator()), indexService: indexService);
            var imported = await importService.ImportAsync(new MediaIngestionRequest(mediaPath, srtPath, "en-US"));
            mediaId = imported.Media.Id;
        }

        var fakeAlignment = new FakeAlignmentProvider(new AlignmentResult(
            [new("hello", 1.0, 1.7), new("world", 1.7, 3.0)],
            [
                new("HH", 1.0, 1.2), new("AH0", 1.2, 1.4), new("L", 1.4, 1.55), new("OW1", 1.55, 1.7),
                new("W", 1.7, 1.9), new("ER1", 1.9, 2.4), new("L", 2.4, 2.7), new("D", 2.7, 3.0),
            ]));

        await using (var realignContext = await factory.CreateDbContextAsync())
        {
            var realignService = new MediaIngestionService(
                realignContext, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(),
                new MediaMetadataProbe(new FFprobeToolLocator()), fakeAlignment, indexService);
            await realignService.RealignAsync(mediaId);
        }

        // Compared against ground truth recomputed independently from the post-realign DB state
        // (same technique as IndexMediaAsync_PopulatesPostingsMatchingTheBuiltStream), not against
        // the pre-realign postings: espeak's predicted "hello world" and the fake aligner's
        // "hello world" phones both canonicalize to similar IPA, so a symbol-overlap check would
        // pass even if RealignAsync left every pre-realign posting in place untouched. Exact
        // equality with a fresh recomputation is the only check that actually distinguishes
        // "rebuilt" from "never touched" here.
        await using var finalContext = await factory.CreateDbContextAsync();
        var postingsAfterRealign = await finalContext.PhoneNGramPostings
            .Where(p => p.MediaId == mediaId)
            .Select(p => new ValueTuple<string, int>(p.NGram, p.StreamPosition))
            .ToListAsync();

        var transcripts = await finalContext.Transcripts
            .Where(t => t.MediaId == mediaId)
            .Include(t => t.Segments).ThenInclude(s => s.Words).ThenInclude(w => w.Phones)
            .ToListAsync();
        var expected = PhoneNGramIndexer.Extract(PhoneStreamBuilder.Build(transcripts).Select(e => e.Token).ToList())
            .Select(o => new ValueTuple<string, int>(o.NGram, o.Position))
            .OrderBy(p => p)
            .ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, postingsAfterRealign.OrderBy(p => p).ToList());
    }

    [Fact]
    public async Task ReindexAllAsync_IsIdempotent()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;
        var factory = services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await ImportAsync(services, phonemizer, _tempDir, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);
        await ImportAsync(services, phonemizer, _tempDir, "b.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);

        var indexService = new PhoneNGramIndexService(factory);

        var firstSummary = await indexService.ReindexAllAsync();
        var firstPostings = await ReadAllPostingsAsync(factory);

        var secondSummary = await indexService.ReindexAllAsync();
        var secondPostings = await ReadAllPostingsAsync(factory);

        Assert.Equal(2, firstSummary.MediaCount);
        Assert.Equal(firstSummary, secondSummary);
        Assert.Equal(firstPostings, secondPostings);
    }

    private static async Task<List<(Guid MediaId, string NGram, int StreamPosition)>> ReadAllPostingsAsync(
        IDbContextFactory<MemeSearcherDbContext> factory)
    {
        await using var context = await factory.CreateDbContextAsync();
        return await context.PhoneNGramPostings
            .OrderBy(p => p.MediaId).ThenBy(p => p.NGram).ThenBy(p => p.StreamPosition)
            .Select(p => new ValueTuple<Guid, string, int>(p.MediaId, p.NGram, p.StreamPosition))
            .ToListAsync();
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
