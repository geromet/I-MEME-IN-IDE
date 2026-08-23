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
/// End-to-end: real espeak-ng phonemization, a real (temp-file) SQLite database, multiple
/// imported media - proving composite search actually stitches clips from different files
/// together, not just that the algorithm works on synthetic tokens (see
/// PhoneticSequenceMatcherTests for that). Skips (returns early) if espeak-ng isn't installed.
/// </summary>
public class CompositeSearchServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-composite-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-composite-test-").FullName;

    private async Task<(Infrastructure.Search.CompositeSearchService Service, EspeakPhonemizer Phonemizer, IDbContextFactory<MemeSearcherDbContext> Factory)?> TrySetUpAsync()
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
        var service = new Infrastructure.Search.CompositeSearchService(dbContextFactory, phonemizer, new Infrastructure.Search.InMemoryQueryPhonemizationCache());

        return (service, phonemizer, dbContextFactory);
    }

    private static async Task<Guid> ImportAsync(
        IDbContextFactory<MemeSearcherDbContext> factory, EspeakPhonemizer phonemizer, string tempDir, string fileName, string srtBody)
    {
        var path = Path.Combine(tempDir, fileName);
        await File.WriteAllTextAsync(path, srtBody);

        var ingestion = new MediaIngestionService(
            await factory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        return result.Media.Id;
    }

    [Fact]
    public async Task SearchAsync_AssemblesAResultFromTwoDifferentMediaFiles()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (service, phonemizer, factory) = setup.Value;

        // Neither file alone contains "superman" - the word only exists split across two clips.
        var mediaA = await ImportAsync(factory, phonemizer, _tempDir, "a.srt", """
            1
            00:00:10,000 --> 00:00:11,000
            super

            """);
        var mediaB = await ImportAsync(factory, phonemizer, _tempDir, "b.srt", """
            1
            00:00:20,000 --> 00:00:21,000
            man

            """);

        var results = await service.SearchAsync("superman", "en-US", new SearchScope.AllIndexedMedia());

        Assert.NotEmpty(results);
        var best = results[0];
        Assert.Equal(2, best.Components.Count);
        Assert.Equal(mediaA, best.Components[0].MediaId);
        Assert.Equal(mediaB, best.Components[1].MediaId);
        Assert.Equal("super", best.Components[0].SourceText);
        Assert.Equal("man", best.Components[1].SourceText);

        // Coverage: the two components should cover disjoint, non-decreasing query ranges.
        Assert.True(best.Components[0].QueryEnd <= best.Components[1].QueryStart + 1);
    }

    /// <summary>
    /// Milestone 10 / issue #4: the existing "AssemblesAResultFromTwoDifferentMediaFiles" test above
    /// imports "super" before "man" - the same order the query needs, so fixed-order concatenation
    /// already satisfies it and proves nothing about any-to-any stitching. Here the import order is
    /// reversed ("man" first, "super" second) while the query still needs "super" before "man": fixed
    /// concatenation can only try [man][super], which does not contain "superman", so finding this
    /// match requires trying the other order too.
    /// </summary>
    [Fact]
    public async Task SearchAsync_FindsAMatchRegardlessOfMediaImportOrder()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (service, phonemizer, factory) = setup.Value;

        var mediaA = await ImportAsync(factory, phonemizer, _tempDir, "a.srt", """
            1
            00:00:10,000 --> 00:00:11,000
            man

            """);
        var mediaB = await ImportAsync(factory, phonemizer, _tempDir, "b.srt", """
            1
            00:00:20,000 --> 00:00:21,000
            super

            """);

        // The candidate ordering pass (#10) reads #9's n-gram index, which this test's own
        // ImportAsync helper (unlike the real ingestion path) does not populate on its own.
        await new Infrastructure.Search.PhoneNGramIndexService(factory).ReindexAllAsync();

        var results = await service.SearchAsync("superman", "en-US", new SearchScope.AllIndexedMedia());

        Assert.NotEmpty(results);
        var best = results[0];
        Assert.Equal(2, best.Components.Count);
        Assert.Equal(mediaB, best.Components[0].MediaId);
        Assert.Equal(mediaA, best.Components[1].MediaId);
        Assert.Equal("super", best.Components[0].SourceText);
        Assert.Equal("man", best.Components[1].SourceText);
    }

    [Fact]
    public async Task SearchAsync_PrefersASingleFileMatchOverACompositeOneWhenBothExist()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (service, phonemizer, factory) = setup.Value;

        var wholeWordMediaId = await ImportAsync(factory, phonemizer, _tempDir, "whole.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            superman

            """);
        await ImportAsync(factory, phonemizer, _tempDir, "split-a.srt", """
            1
            00:00:10,000 --> 00:00:11,000
            super

            """);
        await ImportAsync(factory, phonemizer, _tempDir, "split-b.srt", """
            1
            00:00:20,000 --> 00:00:21,000
            man

            """);

        var results = await service.SearchAsync("superman", "en-US", new SearchScope.AllIndexedMedia());

        Assert.NotEmpty(results);
        var best = results[0];
        Assert.Single(best.Components);
        Assert.Equal(wholeWordMediaId, best.Components[0].MediaId);
    }

    [Fact]
    public async Task SearchAsync_RejectsCompositesWithMoreSourcesThanMaxSourceFiles()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (service, phonemizer, factory) = setup.Value;

        await ImportAsync(factory, phonemizer, _tempDir, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a

            """);
        await ImportAsync(factory, phonemizer, _tempDir, "b.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            b

            """);

        var options = PhoneticSearchOptions.ForMode(SearchMode.SimilarPhonetic) with { MaxSourceFiles = 1, MinimumScore = 0.0 };

        var results = await service.SearchAsync("ab", "en-US", new SearchScope.AllIndexedMedia(), options);

        // With MaxSourceFiles = 1, no result may use more than one source file.
        Assert.All(results, r => Assert.Single(r.Components));
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
