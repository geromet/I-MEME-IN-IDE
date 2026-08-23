using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Alignment;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Services;
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
        services.AddSingleton<SettingsRegistry>();

        services.AddSingleton(TranscriptParserFactory.CreateDefault());
        services.AddScoped<MediaIngestionService>();

        services.AddSingleton<IExternalToolLocator, EspeakToolLocator>();
        services.AddSingleton<IPhonemizer, EspeakPhonemizer>();

        // Milestone 3: not registered as IExternalToolLocator, since only one implementation of
        // that interface can be resolved via DI at a time and espeak already claims it -
        // WhisperXTranscriptionProvider/MediaMetadataProbe depend on their concrete locator types
        // directly instead.
        services.AddSingleton<WhisperXToolLocator>();
        services.AddSingleton<ITranscriptionProvider, WhisperXTranscriptionProvider>();
        // Milestone 6: MFA is the default IAlignmentProvider consumed by
        // MediaIngestionService.RealignAsync, since it's the provider that can produce phone-level
        // timing (addendum §6) - WhisperXAlignmentProvider (Milestone 5, word-level only) stays
        // registered as its own concrete type so it's still available/testable, but no longer
        // claims the shared interface slot.
        services.AddSingleton<WhisperXAlignmentProvider>();
        services.AddSingleton<MfaToolLocator>();
        services.AddSingleton<IAlignmentProvider, MfaAlignmentProvider>();
        services.AddSingleton<FFprobeToolLocator>();
        services.AddSingleton<MediaMetadataProbe>();
        services.AddSingleton<FFmpegToolLocator>();
        services.AddSingleton<FFmpegClipExtractor>();

        // Milestone 7: shared across the app's lifetime (not per-scope) so repeat searches
        // actually benefit from the cache instead of getting a fresh empty one each request.
        services.AddSingleton<IQueryPhonemizationCache, InMemoryQueryPhonemizationCache>();
        services.AddScoped<IPhoneticSearchService, PhoneticSearchService>();
        services.AddScoped<ICompositeSearchService, CompositeSearchService>();
        services.AddScoped<LibraryService>();
        services.AddScoped<SearchHistoryService>();

        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IMediaPlayerLauncher, ExternalMediaPlayerLauncher>();

        services.AddTransient<SearchViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}