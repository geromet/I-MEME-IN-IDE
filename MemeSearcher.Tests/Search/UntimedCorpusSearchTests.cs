using MemeSearcher.Core.Interfaces;
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

namespace MemeSearcher.Tests.Search;

/// <summary>
/// Searching a plain-text corpus must work and must report no timing (#32).
///
/// This is the regression the nullable-timing change created and the existing suite did not
/// catch: the search services unwrapped stream-entry timing with `!`, which was safe only while a
/// stored timing could never be null. Every search test used a timed transcript, so nothing
/// exercised a genuinely untimed one.
/// </summary>
public class UntimedCorpusSearchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-untimed-search-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-untimed-search-").FullName;

    private async Task<(IPhonemizer Phonemizer, IDbContextFactory<MemeSearcherDbContext> Factory)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return null;
        }

        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var phonemizer = new EspeakPhonemizer(locator);
        var path = Path.Combine(_tempDir, "notes.txt");
        await File.WriteAllTextAsync(path, "a long bus\nsomething else entirely\n");

        await using (var context = await factory.CreateDbContextAsync())
        {
            var ingestion = new MediaIngestionService(
                context, TranscriptParserFactory.CreateDefault(), phonemizer,
                new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
            await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        }

        return (phonemizer, factory);
    }

    [Fact]
    public async Task SearchAsync_OverAnUntimedCorpusReturnsResultsWithNullTiming()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;
        var service = new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());

        var results = await service.SearchAsync("among us", "en-US", new SearchScope.AllIndexedMedia());

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.Null(r.StartSeconds);
            Assert.Null(r.EndSeconds);
        });
    }

    [Fact]
    public async Task CompositeSearchAsync_OverAnUntimedCorpusDoesNotThrow()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;
        var service = new CompositeSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());

        var results = await service.SearchAsync("among us", "en-US", new SearchScope.AllIndexedMedia());

        Assert.All(results.SelectMany(r => r.Components), c =>
        {
            Assert.Null(c.StartSeconds);
            Assert.Null(c.EndSeconds);
        });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempDir, recursive: true);
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        GC.SuppressFinalize(this);
    }
}
