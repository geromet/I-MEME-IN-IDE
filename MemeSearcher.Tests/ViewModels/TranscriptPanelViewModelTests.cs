using System.Linq;
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

    /// <summary>Imports "hello world" as one cue, then realigns it with a fake provider carrying real per-word timing - the counterpart to ImportAsync, which leaves timing interpolated.</summary>
    private static async Task<Guid> ImportAndRealignAsync(IDbContextFactory<MemeSearcherDbContext> factory, IPhonemizer phonemizer, string tempDir)
    {
        var mediaPath = Path.Combine(tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "placeholder - never decoded, the aligner is faked");
        var srtPath = Path.Combine(tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:03,000
            hello world

            """);

        await using (var importContext = await factory.CreateDbContextAsync())
        {
            await new MediaIngestionService(
                importContext, TranscriptParserFactory.CreateDefault(), phonemizer,
                new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()))
                .ImportAsync(new MediaIngestionRequest(mediaPath, srtPath, "en-US"));
        }

        var aligner = new FakeAlignmentProvider(new AlignmentResult(
            [new AlignedWord("hello", 1.0, 1.7), new AlignedWord("world", 1.7, 3.0)],
            [
                new AlignedPhone("HH", 1.0, 1.2), new AlignedPhone("AH0", 1.2, 1.4),
                new AlignedPhone("L", 1.4, 1.55), new AlignedPhone("OW1", 1.55, 1.7),
                new AlignedPhone("W", 1.7, 1.9), new AlignedPhone("ER1", 1.9, 2.4),
                new AlignedPhone("L", 2.4, 2.7), new AlignedPhone("D", 2.7, 3.0),
            ]));

        await using var context = await factory.CreateDbContextAsync();
        var media = await context.Media.SingleAsync();
        await new MediaIngestionService(
            context, TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()), aligner)
            .RealignAsync(media.Id);
        return media.Id;
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

        // A plain SRT import has no real per-word timing - MediaIngestionService interpolates it
        // character-proportionally, so Word.IsTimingInterpolated is true. The panel must not point
        // at a specific word on the strength of a guess: it should fall back to the cue-level
        // highlight above rather than lighting up individual words (#26 Part 2).
        Assert.False(tab.Cues[0].HasWordHighlights);
        Assert.All(tab.Cues[0].Words, w => Assert.False(w.IsHighlighted));
    }

    /// <summary>#26 Part 2: once realignment gives a word real (non-interpolated) timing, a match resolved to that word highlights just that word instead of falling back to the whole cue.</summary>
    [Fact]
    public async Task ShowAsync_AfterRealignment_HighlightsTheMatchedWordNotTheWholeCue()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;
        await ImportAndRealignAsync(factory, phonemizer, _tempDir);

        var searchService = new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());
        var results = await searchService.SearchAsync("hello", "en-US", new SearchScope.AllIndexedMedia());
        var row = MakeRow(results.OrderByDescending(r => r.Score).First());

        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));
        await panel.ShowAsync(row);

        var tab = Assert.Single(panel.Tabs);
        var cue = Assert.Single(tab.Cues);
        Assert.True(cue.IsHighlighted);
        Assert.True(cue.HasWordHighlights);

        var hello = Assert.Single(cue.Words, w => w.Text == "hello");
        var world = Assert.Single(cue.Words, w => w.Text == "world");
        Assert.True(hello.IsHighlighted);
        Assert.False(world.IsHighlighted);
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

    /// <summary>#26 part 3, "reverse direction": clicking a matched word raises SeedSearchRequested with that word's exact text, not the normalized cue text.</summary>
    [Fact]
    public async Task SeedSearchFromWordCommand_RaisesSeedSearchRequestedWithTheWordsText()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;
        await ImportAndRealignAsync(factory, phonemizer, _tempDir);

        var searchService = new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache());
        var results = await searchService.SearchAsync("hello", "en-US", new SearchScope.AllIndexedMedia());

        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));
        await panel.ShowAsync(MakeRow(results.OrderByDescending(r => r.Score).First()));

        var cue = Assert.Single(panel.Tabs).Cues[0];
        var world = Assert.Single(cue.Words, w => w.Text == "world");

        string? seeded = null;
        panel.SeedSearchRequested += (_, text) => seeded = text;
        panel.SeedSearchFromWordCommand.Execute(world);

        Assert.Equal("world", seeded);
    }

    /// <summary>#26 part 3's composite-click decision: a component opens (and highlights) only its own media, not every other contributing transcript.</summary>
    [Fact]
    public async Task ShowComponent_OpensOnlyThatComponentsOwnTranscript()
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
        var best = results.OrderByDescending(r => r.Score).First();

        var component = new CompositeComponentRowViewModel(
            new CompositeMatchComponent(
                mediaId, best.StartSeconds, best.EndSeconds, best.SourceText, best.Ipa, best.Phonemes,
                best.Score, best.QueryStart, best.QueryEnd, best.MatchedPhoneDetails),
            "clip.srt", null);

        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));
        await panel.ShowComponentAsync(component);

        var tab = Assert.Single(panel.Tabs);
        Assert.Equal(mediaId, tab.MediaId);
        Assert.True(tab.Cues[0].IsHighlighted);
    }

    [Fact]
    public void ShowComponent_Null_DoesNotOpenAnyTab()
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();
        var panel = new TranscriptPanelViewModel(new TranscriptViewService(factory), new LibraryService(factory));

        panel.ShowComponent(null);

        Assert.Empty(panel.Tabs);
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
