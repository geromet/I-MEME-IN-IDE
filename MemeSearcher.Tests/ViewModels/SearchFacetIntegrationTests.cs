using MemeSearcher.Core.Models;
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
/// #43 executable integration proof: temporary metadata facets are resolved against the real
/// SQLite corpus before either search implementation and both single/composite modes receive the
/// same effective media-ID set.
/// </summary>
public sealed class SearchFacetIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-facets-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-facets-").FullName;

    [Fact]
    public async Task ActiveChannelFacet_NarrowsBothSingleAndCompositeSearchToTheSameDurableSelectionIntersection()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, factory, phonemizer) = setup.Value;
        var alphaSuper = await ImportAsync(factory, phonemizer, "alpha-super.srt", "super", 1);
        var alphaMan = await ImportAsync(factory, phonemizer, "alpha-man.srt", "man", 2);
        var betaSuper = await ImportAsync(factory, phonemizer, "beta-super.srt", "super", 3);
        var betaMan = await ImportAsync(factory, phonemizer, "beta-man.srt", "man", 4);

        await SetMetadataAsync(factory, alphaSuper, "Alpha", YtDlpMediaKind.Audio, new DateOnly(2025, 1, 1));
        await SetMetadataAsync(factory, alphaMan, "Alpha", YtDlpMediaKind.Audio, new DateOnly(2025, 1, 2));
        await SetMetadataAsync(factory, betaSuper, "Beta", YtDlpMediaKind.Audio, new DateOnly(2025, 1, 3));
        await SetMetadataAsync(factory, betaMan, "Beta", YtDlpMediaKind.Audio, new DateOnly(2025, 1, 4));

        // Durable selection excludes one Beta source independently of the temporary facet. The
        // effective scope must remain durable-selection ∩ facet, never overwrite either state.
        var library = new LibraryService(factory);
        await library.SetSelectedAsync(betaMan, false);

        viewModel.FacetChannels = "Alpha";
        viewModel.QueryText = "super";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.Results);
        Assert.All(viewModel.Results, result => Assert.Equal(alphaSuper, result.MediaId));
        Assert.Equal("2 matching / 3 selected of 4 source(s)", viewModel.ScopeSummary);

        viewModel.IsCompositeMode = true;
        viewModel.QueryText = "superman";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.CompositeResults);
        Assert.All(
            viewModel.CompositeResults.SelectMany(result => result.Components),
            component => Assert.Contains(component.MediaId, new[] { alphaSuper, alphaMan }));

        // Temporary filtering must not mutate the durable checkbox state.
        var (selectedIds, total) = await library.GetSelectionSummaryAsync();
        Assert.Equal(4, total);
        Assert.Equal(3, selectedIds.Count);
        Assert.Contains(betaSuper, selectedIds);
        Assert.DoesNotContain(betaMan, selectedIds);
    }

    [Fact]
    public async Task ZeroEffectiveFacetScope_ShortCircuitsBeforePhonemizationAndClearRestoresDurableScope()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, factory, phonemizer) = setup.Value;
        var media = await ImportAsync(factory, phonemizer, "alpha.srt", "among us", 1);
        await SetMetadataAsync(factory, media, "Alpha", YtDlpMediaKind.Video, new DateOnly(2025, 6, 1));

        viewModel.FacetChannels = "Does not exist";
        viewModel.QueryText = "among us";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("", viewModel.QueryIpa);
        Assert.Empty(viewModel.Results);
        Assert.Empty(viewModel.CompositeResults);
        Assert.Equal("0 sources match these filters.", viewModel.StatusMessage);
        Assert.Equal("0 sources match filters (1 selected of 1)", viewModel.ScopeSummary);
        Assert.False(viewModel.CanWidenToFullCorpus);

        await viewModel.ClearFacetsCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasActiveFacets);
        Assert.Equal("All indexed media", viewModel.ScopeSummary);

        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.QueryIpa));
        Assert.Single(viewModel.Results);
        Assert.Equal(media, viewModel.Results[0].MediaId);
    }

    private async Task<(SearchViewModel ViewModel, IDbContextFactory<MemeSearcherDbContext> Factory, EspeakPhonemizer Phonemizer)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return null;
        }

        var services = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var phonemizer = new EspeakPhonemizer(locator);
        var queryCache = new Infrastructure.Search.InMemoryQueryPhonemizationCache();
        var viewModel = new SearchViewModel(
            new Infrastructure.Search.PhoneticSearchService(factory, phonemizer, queryCache),
            new Infrastructure.Search.CompositeSearchService(factory, phonemizer, queryCache),
            phonemizer,
            queryCache,
            new Infrastructure.Search.SearchHistoryService(factory),
            new LibraryService(factory),
            new FakeMediaPlayerLauncher(),
            new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()),
            new FakeFilePickerService(),
            new InMemorySettingsStore());

        return (viewModel, factory, phonemizer);
    }

    private async Task<Guid> ImportAsync(
        IDbContextFactory<MemeSearcherDbContext> factory,
        EspeakPhonemizer phonemizer,
        string fileName,
        string text,
        int second)
    {
        var path = Path.Combine(_tempDir, fileName);
        await File.WriteAllTextAsync(path, $"""
            1
            00:00:{second:00},000 --> 00:00:{second + 1:00},000
            {text}

            """);

        var ingestion = new MediaIngestionService(
            await factory.CreateDbContextAsync(),
            TranscriptParserFactory.CreateDefault(),
            phonemizer,
            new UnusedTranscriptionProvider(),
            new MediaMetadataProbe(new FFprobeToolLocator()));
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        return result.Media.Id;
    }

    private static async Task SetMetadataAsync(
        IDbContextFactory<MemeSearcherDbContext> factory,
        Guid mediaId,
        string channel,
        YtDlpMediaKind mediaKind,
        DateOnly uploadDate)
    {
        await using var context = await factory.CreateDbContextAsync();
        var media = await context.Media.FindAsync(mediaId);
        Assert.NotNull(media);
        media.Channel = channel;
        media.YtDlpMediaKind = mediaKind;
        media.UploadDate = uploadDate;
        await context.SaveChangesAsync();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
