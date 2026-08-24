using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Jobs;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.Core.Settings;
using MemeSearcher.Shell;
using MemeSearcher.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.ViewModels;

/// <summary>
/// Milestone 12's defining behaviour: two executed searches must exist as two independent tabs that
/// can be switched between without re-running either one. Exercises real search services (real
/// espeak-ng, a real temp-file SQLite db) so a passing test means the tabs actually hold distinct
/// search results, not just distinct view-model instances. Skips (returns early) if espeak-ng isn't
/// installed.
/// </summary>
public class MainWindowViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-mainvm-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-mainvm-test-").FullName;

    private async Task<(MainWindowViewModel ViewModel, IDbContextFactory<MemeSearcherDbContext> Factory, EspeakPhonemizer Phonemizer)?> TrySetUpAsync()
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
        var queryCache = new InMemoryQueryPhonemizationCache();
        var searchService = new PhoneticSearchService(dbContextFactory, phonemizer, queryCache);
        var compositeSearchService = new CompositeSearchService(dbContextFactory, phonemizer, queryCache);
        var libraryService = new LibraryService(dbContextFactory);
        var searchHistoryService = new SearchHistoryService(dbContextFactory);
        var settingsStore = new InMemorySettingsStore();

        SearchViewModel SearchViewModelFactory() => new(
            searchService, compositeSearchService, phonemizer, queryCache, searchHistoryService, libraryService,
            new FakeMediaPlayerLauncher(), new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()), new FakeFilePickerService(), settingsStore);

        var jobQueue = new JobQueueService();
        var libraryViewModel = new LibraryViewModel(
            libraryService, new MediaIngestionService(await dbContextFactory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator())),
            new FakeFilePickerService(), settingsStore, new PhoneNGramIndexService(dbContextFactory), jobQueue);
        var settingsViewModel = new SettingsViewModel(new SettingsRegistry([]), settingsStore);
        var jobsPanelViewModel = new JobsPanelViewModel(jobQueue);
        var inspectorViewModel = new InspectorViewModel(new FakeMediaPlayerLauncher());

        IViewPanel[] panels =
        [
            new ViewPanelDescriptor(PanelIds.Library, "Library", DockZone.Left, libraryViewModel),
            new ViewPanelDescriptor(PanelIds.Inspector, "Inspector", DockZone.Right, inspectorViewModel),
            new ViewPanelDescriptor(PanelIds.Jobs, "Jobs / Errors", DockZone.Bottom, jobsPanelViewModel, visibleByDefault: false),
            new ViewPanelDescriptor(PanelIds.Settings, "Settings", DockZone.Right, settingsViewModel, visibleByDefault: false),
        ];

        var viewModel = new MainWindowViewModel(SearchViewModelFactory, libraryViewModel, jobsPanelViewModel, inspectorViewModel, panels, settingsStore);

        return (viewModel, dbContextFactory, phonemizer);
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
    public async Task Constructor_StartsWithExactlyOneActiveSearchTab()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, _, _) = setup.Value;

        var tab = Assert.Single(viewModel.SearchTabs);
        Assert.Same(tab, viewModel.ActiveSearchTab);
    }

    /// <summary>
    /// Milestone 13: the scope indicator must catch up *before* the next search runs, not only
    /// after - otherwise "no matches" from an unnoticed scope filter is indistinguishable from a
    /// query that genuinely has none. Exercises the real production path (a checkbox toggle raises
    /// MediaRowViewModel.IsSelected's PropertyChanged, which LibraryViewModel turns into its own
    /// SelectionSummary change, which MainWindowViewModel fans out to every open tab) rather than
    /// calling RefreshScopeSummaryAsync directly, since that would prove the method works without
    /// proving anything actually calls it when a checkbox changes.
    /// </summary>
    [Fact]
    public async Task TogglingLibrarySelection_RefreshesAnAlreadyOpenTabsScopeSummaryWithoutSearching()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, dbContextFactory, phonemizer) = setup.Value;

        await ImportAsync(dbContextFactory, phonemizer, _tempDir, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            hello world

            """);
        await ImportAsync(dbContextFactory, phonemizer, _tempDir, "b.srt", """
            1
            00:00:10,000 --> 00:00:11,000
            banana split

            """);

        await viewModel.Library.LoadAsync();

        var tab = viewModel.ActiveSearchTab!;
        await tab.RefreshScopeSummaryAsync();
        Assert.Equal("All indexed media", tab.ScopeSummary);

        // No search runs on this tab - only a checkbox toggle in the library panel.
        viewModel.Library.Items[0].IsSelected = false;

        // MainWindowViewModel's reaction is fire-and-forget (matching the rest of this codebase's
        // convention for a UI-triggered write/refresh) - poll briefly rather than assert
        // immediately or rely on a fixed sleep.
        for (var i = 0; i < 20 && tab.ScopeSummary != "1 of 2 source(s)"; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal("1 of 2 source(s)", tab.ScopeSummary);
    }

    [Fact]
    public async Task TwoTabsWithDifferentSearches_HoldIndependentResultsAndSwitchingDoesNotRerun()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, dbContextFactory, phonemizer) = setup.Value;

        await ImportAsync(dbContextFactory, phonemizer, _tempDir, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            hello world

            """);
        await ImportAsync(dbContextFactory, phonemizer, _tempDir, "b.srt", """
            1
            00:00:10,000 --> 00:00:11,000
            banana split

            """);

        var firstTab = viewModel.ActiveSearchTab!;
        firstTab.QueryText = "hello world";
        await firstTab.SearchCommand.ExecuteAsync(null);
        Assert.Single(firstTab.Results);
        Assert.Equal("hello world", firstTab.Results[0].SourceText);

        viewModel.NewSearchTabCommand.Execute(null);
        Assert.Equal(2, viewModel.SearchTabs.Count);
        var secondTab = viewModel.ActiveSearchTab!;
        Assert.NotSame(firstTab, secondTab);

        secondTab.QueryText = "banana split";
        await secondTab.SearchCommand.ExecuteAsync(null);
        Assert.Single(secondTab.Results);
        Assert.Equal("banana split", secondTab.Results[0].SourceText);

        // The defining behaviour: switching the active tab back to the first one must not have
        // disturbed its results, and must not require re-running its search to see them again.
        viewModel.ActiveSearchTab = firstTab;
        Assert.Single(firstTab.Results);
        Assert.Equal("hello world", firstTab.Results[0].SourceText);
        Assert.Equal("hello world", firstTab.QueryText);

        viewModel.ActiveSearchTab = secondTab;
        Assert.Single(secondTab.Results);
        Assert.Equal("banana split", secondTab.Results[0].SourceText);
    }

    [Fact]
    public async Task CloseSearchTab_ClosingTheLastTabOpensAFreshOne()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, _, _) = setup.Value;

        var onlyTab = viewModel.ActiveSearchTab!;
        viewModel.CloseSearchTabCommand.Execute(onlyTab);

        var replacement = Assert.Single(viewModel.SearchTabs);
        Assert.NotSame(onlyTab, replacement);
        Assert.Same(replacement, viewModel.ActiveSearchTab);
    }

    [Fact]
    public async Task CloseSearchTab_ClosingTheActiveTabActivatesAnotherOne()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, _, _) = setup.Value;

        var firstTab = viewModel.ActiveSearchTab!;
        viewModel.NewSearchTabCommand.Execute(null);
        var secondTab = viewModel.ActiveSearchTab!;

        viewModel.CloseSearchTabCommand.Execute(secondTab);

        Assert.Same(firstTab, Assert.Single(viewModel.SearchTabs));
        Assert.Same(firstTab, viewModel.ActiveSearchTab);
    }

    /// <summary>
    /// #19 exit criterion: layout (open/closed) persists, and a zone's chrome disappears entirely
    /// once nothing in it is visible rather than showing an empty pane.
    /// </summary>
    [Fact]
    public async Task JobsPanel_StartsHiddenByDefault_AndTogglingItMovesItInAndOutOfTheBottomZone()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, _, _) = setup.Value;

        Assert.False(viewModel.HasBottomPanels);
        Assert.Empty(viewModel.BottomPanels);

        viewModel.ToggleJobsPanelCommand.Execute(null);

        Assert.True(viewModel.HasBottomPanels);
        var jobsSlot = Assert.Single(viewModel.BottomPanels);
        Assert.Equal("Jobs / Errors", jobsSlot.DisplayName);

        viewModel.ToggleJobsPanelCommand.Execute(null);

        Assert.False(viewModel.HasBottomPanels);
        Assert.Empty(viewModel.BottomPanels);
    }

    /// <summary>The View menu binds directly to PanelSlotViewModel.IsVisible - toggling it (as the menu's checkbox would) must move the panel between zones exactly like the dedicated Jobs toolbar toggle does.</summary>
    [Fact]
    public async Task TogglingAPanelSlotsIsVisible_MovesItInAndOutOfItsZone()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, _, _) = setup.Value;

        Assert.True(viewModel.HasRightPanels);
        var inspectorSlot = Assert.Single(viewModel.RightPanels);
        Assert.Equal("Inspector", inspectorSlot.DisplayName);

        inspectorSlot.IsVisible = false;

        Assert.False(viewModel.HasRightPanels);
        Assert.Empty(viewModel.RightPanels);

        inspectorSlot.IsVisible = true;

        Assert.True(viewModel.HasRightPanels);
        Assert.Same(inspectorSlot, Assert.Single(viewModel.RightPanels));
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
