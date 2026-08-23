using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Search;

/// <summary>
/// End-to-end: real espeak-ng phonemization, a real (temp-file) SQLite database, and the actual
/// matcher - exercising the docs' own canonical examples (handoff §52) rather than synthetic
/// tokens, since that's where an assumption from any one layer could quietly break another.
/// Skips (returns early) if espeak-ng isn't installed on the machine running the tests.
/// </summary>
public class PhoneticSearchServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-search-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-search-test-").FullName;

    private async Task<(IPhonemizer Phonemizer, IServiceProvider Services)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
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

    private static async Task ImportAsync(
        IServiceProvider services, IPhonemizer phonemizer, string tempDir, string fileName, string srtBody)
    {
        var path = Path.Combine(tempDir, fileName);
        await File.WriteAllTextAsync(path, srtBody);

        await using var context = await services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>().CreateDbContextAsync();
        var ingestion = new MediaIngestionService(context, TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
    }

    [Fact]
    public async Task SearchAsync_FindsATextuallyDifferentButPhoneticallySimilarMatch()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        // handoff §52's own example: "among us" should find "a long bus".
        await ImportAsync(services, phonemizer, _tempDir, "clip.srt", """
            1
            00:00:10,000 --> 00:00:12,000
            a long bus

            """);

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>(), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("among us", "en-US", new SearchScope.AllIndexedMedia());

        var result = Assert.Single(results);
        Assert.Equal("a long bus", result.SourceText);
        Assert.True(result.Score > 0.5, $"expected a reasonably high score, got {result.Score}");
        Assert.Equal(10.0, result.StartSeconds, 1);
    }

    [Fact]
    public async Task SearchAsync_MatchesAcrossWordBoundaries()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        // handoff §12: "ice cream" and "I scream" must be comparable despite the word split moving.
        await ImportAsync(services, phonemizer, _tempDir, "clip.srt", """
            1
            00:00:05,000 --> 00:00:06,500
            I scream

            """);

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>(), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("ice cream", "en-US", new SearchScope.AllIndexedMedia());

        var result = Assert.Single(results);
        Assert.Equal("i scream", result.SourceText); // normalized text is lowercased
        Assert.True(result.Score > 0.8, $"expected a near-exact score, got {result.Score}");
    }

    [Fact]
    public async Task SearchAsync_UnrelatedQueryReturnsNoResults()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        await ImportAsync(services, phonemizer, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>(), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("supercalifragilisticexpialidocious", "en-US", new SearchScope.AllIndexedMedia());

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ArbitraryNonDictionaryQueryDoesNotThrow()
    {
        // handoff §40/§41: query does not need to be a real word.
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        await ImportAsync(services, phonemizer, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>(), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("zzyzx blorp", "en-US", new SearchScope.AllIndexedMedia());

        Assert.NotNull(results);
    }

    [Fact]
    public async Task SearchAsync_SingleMediaScopeOnlySearchesThatMedia()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, services) = setup.Value;

        // Different content (not just different filenames) so the two imports get distinct
        // content hashes rather than deduping to one Media (addendum §3-4).
        await ImportAsync(services, phonemizer, _tempDir, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);
        await ImportAsync(services, phonemizer, _tempDir, "b.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us again

            """);

        await using var context = await services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>().CreateDbContextAsync();
        var mediaIds = await context.Media.Select(m => m.Id).ToListAsync();
        Assert.Equal(2, mediaIds.Count);

        var searchService = new Infrastructure.Search.PhoneticSearchService(
            services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>(), phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        var results = await searchService.SearchAsync("among us", "en-US", new SearchScope.SingleMedia(mediaIds[0]));

        Assert.All(results, r => Assert.Equal(mediaIds[0], r.MediaId));
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
