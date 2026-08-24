using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Jobs;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Alignment;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Templates;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Jobs;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Infrastructure.YtDlp;
using MemeSearcher.Services;
using MemeSearcher.Shell;
using MemeSearcher.ViewModels;
using MemeSearcher.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        using (var scope = Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MemeSearcherDbContext>().Database.Migrate();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // AddDbContextFactory also registers MemeSearcherDbContext itself as scoped (resolved via
        // the factory), so MediaIngestionService can keep injecting the DbContext directly while
        // PhoneticSearchService uses the factory to safely fan out across media in parallel.
        services.AddDbContextFactory<MemeSearcherDbContext>(options =>
            options.UseSqlite(DatabasePathProvider.GetDefaultConnectionString()));

        // Milestone 19 (#24): settings are a singleton store plus a set of categories resolved as
        // IEnumerable<ISettingsCategory> - so a new category is one AddSingleton call and the
        // Settings UI never changes. Note this is also the pattern #16 wants for tool locators:
        // resolving a collection has none of the one-implementation-per-interface problem.
        services.AddSingleton<ISettingsStore>(_ => JsonSettingsStore.CreateDefault());
        services.AddSingleton<CudaAvailabilityProbe>();
        services.AddSingleton<WhisperXSettings>();
        services.AddSingleton<ISettingsCategory>(sp => sp.GetRequiredService<WhisperXSettings>());
        services.AddSingleton<ExternalToolSettings>();
        services.AddSingleton<ISettingsCategory>(sp => sp.GetRequiredService<ExternalToolSettings>());
        services.AddSingleton<MfaModelProbe>();
        services.AddSingleton(sp => new MfaSettings(
            sp.GetRequiredService<MfaModelProbe>(), sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<ISettingsCategory>(sp => sp.GetRequiredService<MfaSettings>());
        services.AddSingleton<YtDlpSettings>();
        services.AddSingleton<ISettingsCategory>(sp => sp.GetRequiredService<YtDlpSettings>());
        services.AddSingleton<SettingsRegistry>();

        services.AddSingleton(TranscriptParserFactory.CreateDefault());
        // #9: independently registered so IndexMediaAsync/ReindexAllAsync are callable on their
        // own (e.g. a repair run) - MediaIngestionService only invokes it, doesn't own it.
        services.AddScoped<IPhoneNGramIndexService, PhoneNGramIndexService>();
        services.AddScoped<MediaIngestionService>();

        // Milestone 14: import/realign/reindex become queued jobs rather than direct awaits, so
        // the concurrency limit here is what stops queuing ten whisperx runs from launching ten
        // at once (#14). One shared instance for the app's lifetime - it *is* the queue.
        services.AddSingleton<IJobQueue>(_ => new JobQueueService(maxConcurrency: 1));

        // #16: every locator is keyed under its own ToolName, all against the one IExternalToolLocator
        // interface - previously only one implementation could ever claim that interface (DI
        // resolves the last registration), so every locator but espeak was registered as its own
        // concrete type instead, and every consumer depended on that concrete type directly. A
        // consumer now asks for "the locator keyed <name>" via [FromKeyedServices(...)] on its own
        // constructor parameter (still typed IExternalToolLocator) - the interface finally earns its
        // keep, adding a sixth tool is one AddKeyedSingleton line instead of a repeated dance, and
        // GetKeyedServices(KeyedService.AnyKey) below enumerates every one of them without a
        // hand-maintained list that a forgotten line would silently fall out of.
        //
        // Every locator gets the settings store and the tool category: without them a locator
        // silently ignores its configured path and keeps reporting "not found on PATH".
        services.AddKeyedSingleton<IExternalToolLocator>("espeak-ng", (sp, _) => new EspeakToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddSingleton<IPhonemizer, EspeakPhonemizer>();

        services.AddKeyedSingleton<IExternalToolLocator>("whisperx", (sp, _) => new WhisperXToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddSingleton<ITranscriptionProvider, WhisperXTranscriptionProvider>();
        // Milestone 6: MFA is the default IAlignmentProvider consumed by
        // MediaIngestionService.RealignAsync, since it's the provider that can produce phone-level
        // timing (addendum §6) - WhisperXAlignmentProvider (Milestone 5, word-level only) stays
        // registered as its own concrete type so it's still available/testable, but no longer
        // claims the shared interface slot.
        services.AddSingleton<WhisperXAlignmentProvider>();
        services.AddKeyedSingleton<IExternalToolLocator>("mfa", (sp, _) => new MfaToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddSingleton<IAlignmentProvider, MfaAlignmentProvider>();
        services.AddKeyedSingleton<IExternalToolLocator>("ffprobe", (sp, _) => new FFprobeToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddSingleton<MediaMetadataProbe>();
        services.AddKeyedSingleton<IExternalToolLocator>("ffmpeg", (sp, _) => new FFmpegToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddSingleton<FFmpegClipExtractor>();
        // #27: the sixth tool #16 was written to make easy - one AddKeyedSingleton line, no
        // companion concrete-type registration needed anywhere.
        services.AddKeyedSingleton<IExternalToolLocator>("yt-dlp", (sp, _) => new YtDlpToolLocator(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<ExternalToolSettings>()));
        services.AddScoped<YtDlpPlaylistEnumerationService>();
        services.AddScoped<YtDlpImportPlanner>();
        services.AddScoped<YtDlpDownloadProvider>();

        services.AddSingleton<IToolRegistry>(sp => new ToolRegistry(
            [.. sp.GetKeyedServices<IExternalToolLocator>(KeyedService.AnyKey)]));

        // Milestone 7: shared across the app's lifetime (not per-scope) so repeat searches
        // actually benefit from the cache instead of getting a fresh empty one each request.
        services.AddSingleton<IQueryPhonemizationCache, InMemoryQueryPhonemizationCache>();
        services.AddScoped<IPhoneticSearchService, PhoneticSearchService>();
        services.AddScoped<ICompositeSearchService, CompositeSearchService>();
        services.AddScoped<LibraryService>();
        services.AddScoped<SearchHistoryService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<TemplateService>();
        services.AddScoped<TemplateSearchService>();
        services.AddScoped<TemplateImportExportService>();
        services.AddScoped<TranscriptViewService>();

        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IMediaPlayerLauncher, ExternalMediaPlayerLauncher>();

        services.AddTransient<SearchViewModel>();
        // Milestone 12: MainWindowViewModel needs to mint a new SearchViewModel per opened tab -
        // a delegate keeps it from depending on IServiceProvider directly (which would make it a
        // service locator and hard to unit-test without a full container).
        services.AddSingleton<Func<SearchViewModel>>(sp => sp.GetRequiredService<SearchViewModel>);

        // #19: Library/Inspector/Jobs/Settings are now registered as IViewPanel, resolved as
        // IEnumerable<IViewPanel> the same way ISettingsCategory is above - a new panel is one
        // AddSingleton pair, no shell XAML edit. They must be singletons: the shell wraps each one
        // in a PanelSlotViewModel that subscribes to it, and a second instance (e.g. under
        // AddTransient) would mean a second, independent subscription - JobsPanelViewModel in
        // particular subscribes to IJobQueue.Changed in its constructor and would double its rows.
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<JobsPanelViewModel>();
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<TranscriptPanelViewModel>();
        services.AddSingleton<CatalogsViewModel>();
        services.AddSingleton<IViewPanel>(sp => new ViewPanelDescriptor(
            PanelIds.Library, "Library", DockZone.Left, sp.GetRequiredService<LibraryViewModel>()));
        services.AddSingleton<IViewPanel>(sp => new ViewPanelDescriptor(
            PanelIds.Inspector, "Inspector", DockZone.Right, sp.GetRequiredService<InspectorViewModel>()));
        // DockZone.Bottom, not Right: Inspector already occupies Right, and that zone's TabControl
        // only shows one tab at a time. #26's whole point is a phones-view (Inspector) plus a
        // lines-view (Transcript) visible together, not fighting over the same tab slot.
        services.AddSingleton<IViewPanel>(sp => new ViewPanelDescriptor(
            PanelIds.Transcript, "Transcript", DockZone.Bottom, sp.GetRequiredService<TranscriptPanelViewModel>()));
        services.AddSingleton<IViewPanel>(sp => new ViewPanelDescriptor(
            PanelIds.Jobs, "Jobs / Errors", DockZone.Bottom, sp.GetRequiredService<JobsPanelViewModel>(), visibleByDefault: false));
        services.AddSingleton<IViewPanel>(sp => new ViewPanelDescriptor(
            PanelIds.Settings, "Settings", DockZone.Right, sp.GetRequiredService<SettingsViewModel>(), visibleByDefault: false));
        services.AddSingleton<IViewPanel>(sp => new ViewPanelDescriptor(
            PanelIds.Catalogs, "Catalogs", DockZone.Left, sp.GetRequiredService<CatalogsViewModel>(), visibleByDefault: false));
        services.AddSingleton<TemplatesViewModel>();
        services.AddSingleton<IViewPanel>(sp => new ViewPanelDescriptor(
            PanelIds.Templates, "Templates", DockZone.Left, sp.GetRequiredService<TemplatesViewModel>(), visibleByDefault: false));

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}