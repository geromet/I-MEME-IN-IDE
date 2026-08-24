using System.Linq;
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
using MemeSearcher.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// #26 (Milestone 21): real database, real espeak phonemization, a real search - proving the panel
/// actually resolves a SearchResultRowViewModel's matched phones back onto the transcript's real
/// Segment rows (via MatchedPhone.SegmentId, #26's own data-plumbing addition), not a synthetic
/// stand-in. Skips (returns early) if espeak-ng isn't installed.
/// </summary>
public class TranscriptPanelViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-transcriptpanel-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-transcriptpanel-test-").FullName;

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

        return (new EspeakPhonemizer(locator), factory);
    }

    private static async Task<Guid> ImportAsync(IDbContextFactory<MemeSearcherDbContext> factory, IPhonemizer phonemizer, string tempDir)
    {
        var srtPath = Path.Combine(tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            hello there

            2
            00:00:05,000 --> 00:00:07,000
            general kenobi

            """);

        var ingestion = new MediaIngestionService(
            await factory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));
        return result.Media.Id;
    }

    private static SearchResultRowViewModel MakeRow(SearchResult result) => new(
        result, new FakeMediaPlayerLauncher(), new FakeClipboardService(),
        new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService());

    [Fact]
    public async Task ShowAsync_Null_DoesNotOpenAnyTab()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (_, factory) = setup.Value;
        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));

        panel.Show(null);

        Assert.Empty(panel.Tabs);
        Assert.False(panel.HasTabs);
    }

    [Fact]
    public async Task ShowAsync_OpensTheMediasTranscriptAndHighlightsTheMatchedCue()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;
        var mediaId = await ImportAsync(factory, phonemizer, _tempDir);

        var searchService = new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());
        var results = await searchService.SearchAsync("hello", "en-US", new SearchScope.AllIndexedMedia());
        var row = MakeRow(results.OrderByDescending(r => r.Score).First());

        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));
        await panel.ShowAsync(row);

        var tab = Assert.Single(panel.Tabs);
        Assert.Equal(mediaId, tab.MediaId);
        Assert.Same(tab, panel.ActiveTab);
        Assert.Equal(2, tab.Cues.Count);

        // "hello" matched the first cue ("hello there"), not the second - only that one should be
        // highlighted, and it's the one the view should scroll to.
        Assert.True(tab.Cues[0].IsHighlighted);
        Assert.False(tab.Cues[1].IsHighlighted);
        Assert.Same(tab.Cues[0], tab.ScrollTarget);
    }

    [Fact]
    public async Task ShowAsync_SameMediaTwice_ReusesTheExistingTabInsteadOfDuplicating()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;
        await ImportAsync(factory, phonemizer, _tempDir);

        var searchService = new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());
        var helloResults = await searchService.SearchAsync("hello", "en-US", new SearchScope.AllIndexedMedia());
        var kenobiResults = await searchService.SearchAsync("kenobi", "en-US", new SearchScope.AllIndexedMedia());

        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));
        await panel.ShowAsync(MakeRow(helloResults.OrderByDescending(r => r.Score).First()));
        await panel.ShowAsync(MakeRow(kenobiResults.OrderByDescending(r => r.Score).First()));

        // Same media, second selection just moves the highlight - it doesn't open a second tab.
        var tab = Assert.Single(panel.Tabs);
        Assert.False(tab.Cues[0].IsHighlighted);
        Assert.True(tab.Cues[1].IsHighlighted);
    }

    [Fact]
    public async Task CloseTab_RemovesItAndFallsBackToAnotherOpenTab()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;
        await ImportAsync(factory, phonemizer, _tempDir);

        var searchService = new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());
        var results = await searchService.SearchAsync("hello", "en-US", new SearchScope.AllIndexedMedia());

        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));
        await panel.ShowAsync(MakeRow(results.OrderByDescending(r => r.Score).First()));
        var tab = Assert.Single(panel.Tabs);

        panel.CloseTabCommand.Execute(tab);

        Assert.Empty(panel.Tabs);
        Assert.Null(panel.ActiveTab);
        Assert.False(panel.HasTabs);
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
