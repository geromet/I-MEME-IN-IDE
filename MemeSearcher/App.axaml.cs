using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
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
        // Milestone 5: standalone alignment provider (not yet wired into MediaIngestionService's
        // file-transcript path - see issue tracking for why). Registered so it's available/testable.
        services.AddSingleton<IAlignmentProvider, WhisperXAlignmentProvider>();
        services.AddSingleton<FFprobeToolLocator>();
        services.AddSingleton<MediaMetadataProbe>();
        services.AddSingleton<FFmpegToolLocator>();
        services.AddSingleton<FFmpegClipExtractor>();

        services.AddScoped<IPhoneticSearchService, PhoneticSearchService>();
        services.AddScoped<ICompositeSearchService, CompositeSearchService>();
        services.AddScoped<LibraryService>();

        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IMediaPlayerLauncher, ExternalMediaPlayerLauncher>();

        services.AddTransient<SearchViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}