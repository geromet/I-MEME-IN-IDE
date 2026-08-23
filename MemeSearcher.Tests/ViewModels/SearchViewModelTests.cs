using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// Exercises SearchViewModel against real services (real espeak-ng, a real temp-file SQLite db) -
/// proving the ViewModel actually wires SearchCommand through to the underlying pipeline, not
/// just that it compiles. Skips (returns early) if espeak-ng isn't installed.
/// </summary>
public class SearchViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-searchvm-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-searchvm-test-").FullName;

    private async Task<(SearchViewModel ViewModel, IDbContextFactory<MemeSearcherDbContext> Factory, EspeakPhonemizer Phonemizer)?> TrySetUpAsync()
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
        var dbContextFactory = services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await dbContextFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var phonemizer = new EspeakPhonemizer(locator);
        var queryCache = new Infrastructure.Search.InMemoryQueryPhonemizationCache();
        var searchService = new Infrastructure.Search.PhoneticSearchService(dbContextFactory, phonemizer, queryCache);
        var compositeSearchService = new Infrastructure.Search.CompositeSearchService(dbContextFactory, phonemizer, queryCache);
        var libraryService = new LibraryService(dbContextFactory);
        var searchHistoryService = new Infrastructure.Search.SearchHistoryService(dbContextFactory);
        var viewModel = new SearchViewModel(
            searchService, compositeSearchService, phonemizer, queryCache, searchHistoryService, libraryService,
            new FakeMediaPlayerLauncher(), new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService());

        return (viewModel, dbContextFactory, phonemizer);
    }

    [Fact]
    public async Task SearchAsync_AfterImport_PopulatesResultsAndQueryIpa()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, dbContextFactory, phonemizer) = setup.Value;

        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);

        var ingestion = new MediaIngestionService(await dbContextFactory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        await ingestion.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));

        viewModel.QueryText = "among us";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.QueryIpa));
        Assert.Single(viewModel.Results);
        Assert.Equal("a long bus", viewModel.Results[0].SourceText);
        Assert.Contains("1 result", viewModel.StatusMessage);

        // A transcript-only import has no playable media - MediaPath must resolve to null
        // (not the transcript file), so Play stays disabled instead of trying to hand a
        // subtitle file to a media player.
        Assert.Null(viewModel.Results[0].MediaPath);
        Assert.False(viewModel.Results[0].PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchAsync_WithAttachedMediaFile_ResolvesPlayableMediaPath()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, dbContextFactory, phonemizer) = setup.Value;

        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);
        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");

        var ingestion = new MediaIngestionService(await dbContextFactory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        await ingestion.ImportAsync(new MediaIngestionRequest(mediaPath, srtPath, "en-US"));

        viewModel.QueryText = "among us";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Results);
        Assert.Equal(mediaPath, viewModel.Results[0].MediaPath);
        Assert.True(viewModel.Results[0].PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchAsync_RecordsSearchHistoryAndSupportsRerunningIt()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, dbContextFactory, phonemizer) = setup.Value;

        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);

        var ingestion = new MediaIngestionService(await dbContextFactory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        await ingestion.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));

        viewModel.QueryText = "among us";
        await viewModel.SearchCommand.ExecuteAsync(null);

        var entry = Assert.Single(viewModel.RecentSearches);
        Assert.Equal("among us", entry.QueryText);
        Assert.False(entry.IsComposite);
        Assert.Equal(1, entry.ResultCount);

        // Clear the current results, then rerun purely from the history entry - the entry alone
        // (not any cached SearchViewModel state) must be enough to reproduce the search.
        viewModel.QueryText = "";
        viewModel.Results.Clear();

        await viewModel.RerunSearchCommand.ExecuteAsync(entry);

        Assert.Equal("among us", viewModel.QueryText);
        Assert.Single(viewModel.Results);
        Assert.Equal("a long bus", viewModel.Results[0].SourceText);
    }

    [Fact]
    public async Task SearchAsync_InCompositeMode_PopulatesCompositeResultsInsteadOfResults()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, dbContextFactory, phonemizer) = setup.Value;

        await ImportAsync(dbContextFactory, phonemizer, _tempDir, "a.srt", """
            1
            00:00:10,000 --> 00:00:11,000
            super

            """);
        await ImportAsync(dbContextFactory, phonemizer, _tempDir, "b.srt", """
            1
            00:00:20,000 --> 00:00:21,000
            man

            """);

        viewModel.IsCompositeMode = true;
        viewModel.QueryText = "superman";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Results);
        Assert.NotEmpty(viewModel.CompositeResults);
        Assert.Equal(2, viewModel.CompositeResults[0].Components.Count);
        // Titles, not raw GUIDs.
        Assert.Equal("a.srt", viewModel.CompositeResults[0].Components[0].MediaTitle);
        Assert.Equal("b.srt", viewModel.CompositeResults[0].Components[1].MediaTitle);
    }

    private static async Task ImportAsync(
        IDbContextFactory<MemeSearcherDbContext> factory, EspeakPhonemizer phonemizer, string tempDir, string fileName, string srtBody)
    {
        var path = Path.Combine(tempDir, fileName);
        await File.WriteAllTextAsync(path, srtBody);

        var ingestion = new MediaIngestionService(
            await factory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
    }

    [Fact]
    public void SearchCommand_CannotExecuteWithEmptyQuery()
    {
        var locator = new EspeakToolLocator();
        var phonemizer = new EspeakPhonemizer(locator);
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        var queryCache = new Infrastructure.Search.InMemoryQueryPhonemizationCache();
        var viewModel = new SearchViewModel(
            new Infrastructure.Search.PhoneticSearchService(dbContextFactory, phonemizer, queryCache),
            new Infrastructure.Search.CompositeSearchService(dbContextFactory, phonemizer, queryCache),
            phonemizer,
            queryCache,
            new Infrastructure.Search.SearchHistoryService(dbContextFactory),
            new LibraryService(dbContextFactory),
            new FakeMediaPlayerLauncher(),
            new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()),
            new FakeFilePickerService());

        Assert.False(viewModel.SearchCommand.CanExecute(null));

        viewModel.QueryText = "among us";
        Assert.True(viewModel.SearchCommand.CanExecute(null));

        viewModel.QueryText = "   ";
        Assert.False(viewModel.SearchCommand.CanExecute(null));
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
