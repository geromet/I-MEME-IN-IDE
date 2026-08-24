using MemeSearcher.Core.Jobs;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Jobs;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Services;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Catalogs;

/// <summary>
/// Milestone 17 (#20) exit criterion: "a catalog can be created, populated, and searched" and "a
/// catalog-scoped search records the catalog in ScopeDescription" - end to end through
/// CatalogsViewModel and SearchViewModel sharing one real LibraryService, the way App.axaml.cs's DI
/// graph actually wires them (see LibraryServiceSharingTests for why that sharing holds).
/// </summary>
public class CatalogScopeIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-catalogscope-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-catalogscope-test-").FullName;

    private class StubFilePickerService : IFilePickerService
    {
        public Task<IReadOnlyList<string>> PickMediaFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickClipExportPathAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickTemplateExportPathAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickTemplateImportPathAsync() => Task.FromResult<string?>(null);
    }

    private async Task<Guid> ImportAsync(IDbContextFactory<MemeSearcherDbContext> factory, string fileName, string srtBody)
    {
        var path = Path.Combine(_tempDir, fileName);
        await File.WriteAllTextAsync(path, srtBody);

        var phonemizer = new EspeakPhonemizer(new EspeakToolLocator());
        var ingestion = new MediaIngestionService(await factory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        return result.Media.Id;
    }

    [Fact]
    public async Task ApplyingACatalog_ScopesASearchTabAndLabelsItsScopeSummary()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return;
        }

        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await dbContextFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var keptId = await ImportAsync(dbContextFactory, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);
        var excludedId = await ImportAsync(dbContextFactory, "b.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);

        // One shared LibraryService, exactly like the app's single root-resolved instance.
        var libraryService = new LibraryService(dbContextFactory);
        var catalogService = new CatalogService(dbContextFactory);

        var libraryViewModel = new LibraryViewModel(
            libraryService,
            new MediaIngestionService(await dbContextFactory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), new EspeakPhonemizer(locator), new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator())),
            new StubFilePickerService(),
            new InMemorySettingsStore(),
            new PhoneNGramIndexService(dbContextFactory),
            new JobQueueService());

        var catalogsViewModel = new CatalogsViewModel(catalogService, libraryService, libraryViewModel);

        var phonemizer = new EspeakPhonemizer(locator);
        var queryCache = new InMemoryQueryPhonemizationCache();
        var searchViewModel = new SearchViewModel(
            new PhoneticSearchService(dbContextFactory, phonemizer, queryCache),
            new CompositeSearchService(dbContextFactory, phonemizer, queryCache),
            phonemizer, queryCache, new SearchHistoryService(dbContextFactory), libraryService,
            new FakeMediaPlayerLauncher(), new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService(),
            new InMemorySettingsStore());

        var catalogId = await catalogService.CreateAsync("Kept only", null);
        await catalogService.SetMemberAsync(catalogId, keptId, true);

        var catalogRow = new CatalogRowViewModel(Assert.Single(await catalogService.GetAllAsync()));
        await catalogsViewModel.ApplyToSearchCommand.ExecuteAsync(catalogRow);

        await searchViewModel.RefreshScopeSummaryCommand.ExecuteAsync(null);
        Assert.Equal("Catalog: Kept only (1 source(s))", searchViewModel.ScopeSummary);

        searchViewModel.QueryText = "among us";
        await searchViewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(searchViewModel.Results);
        Assert.DoesNotContain(searchViewModel.Results, r => r.MediaId == excludedId);
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
