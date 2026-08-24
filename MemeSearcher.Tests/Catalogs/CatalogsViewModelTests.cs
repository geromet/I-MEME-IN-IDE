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

/// <summary>Exercises CatalogsViewModel's create/rename/delete commands against real services (real espeak-ng, a real temp-file SQLite db). Skips (returns early) if espeak-ng isn't installed.</summary>
public class CatalogsViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-catalogsvm-test-{Guid.NewGuid():N}.db");

    private class StubFilePickerService : IFilePickerService
    {
        public Task<IReadOnlyList<string>> PickMediaFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickClipExportPathAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    }

    private async Task<CatalogsViewModel?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
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

        var libraryService = new LibraryService(dbContextFactory);
        var catalogService = new CatalogService(dbContextFactory);
        var libraryViewModel = new LibraryViewModel(
            libraryService,
            new MediaIngestionService(await dbContextFactory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), new EspeakPhonemizer(locator), new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator())),
            new StubFilePickerService(),
            new InMemorySettingsStore(),
            new PhoneNGramIndexService(dbContextFactory),
            new JobQueueService());

        return new CatalogsViewModel(catalogService, libraryService, libraryViewModel);
    }

    [Fact]
    public async Task CreateAsync_AddsACatalogWithTheGivenNameAndDescription()
    {
        var viewModel = await TrySetUpAsync();
        if (viewModel is null)
        {
            return;
        }

        viewModel.NewCatalogName = "Vine compilations";
        viewModel.NewCatalogDescription = "Short-form clips";
        await viewModel.CreateCommand.ExecuteAsync(null);

        var catalog = Assert.Single(viewModel.Catalogs);
        Assert.Equal("Vine compilations", catalog.Name);
        Assert.Equal("Short-form clips", catalog.Description);

        // The input fields clear so the next create doesn't accidentally repeat this one.
        Assert.Equal("", viewModel.NewCatalogName);
        Assert.Equal("", viewModel.NewCatalogDescription);
    }

    [Fact]
    public async Task BeginRenameThenSaveRenameAsync_UpdatesTheCatalogsNameAndDescription()
    {
        var viewModel = await TrySetUpAsync();
        if (viewModel is null)
        {
            return;
        }

        viewModel.NewCatalogName = "Season 3";
        await viewModel.CreateCommand.ExecuteAsync(null);
        var catalog = Assert.Single(viewModel.Catalogs);

        viewModel.BeginRenameCommand.Execute(catalog);
        Assert.True(catalog.IsEditing);
        Assert.Equal("Season 3", catalog.EditName);

        catalog.EditName = "Season 3 (renamed)";
        catalog.EditDescription = "Now with a description";
        await viewModel.SaveRenameCommand.ExecuteAsync(catalog);

        // LoadAsync rebuilds the row - re-fetch it rather than trusting the stale `catalog` reference.
        var renamed = Assert.Single(viewModel.Catalogs);
        Assert.Equal("Season 3 (renamed)", renamed.Name);
        Assert.Equal("Now with a description", renamed.Description);
        Assert.False(renamed.IsEditing);
    }

    [Fact]
    public async Task SaveRenameAsync_RejectsABlankName_LeavingTheOriginalNameInPlace()
    {
        var viewModel = await TrySetUpAsync();
        if (viewModel is null)
        {
            return;
        }

        viewModel.NewCatalogName = "Season 3";
        await viewModel.CreateCommand.ExecuteAsync(null);
        var catalog = Assert.Single(viewModel.Catalogs);

        viewModel.BeginRenameCommand.Execute(catalog);
        catalog.EditName = "   ";
        await viewModel.SaveRenameCommand.ExecuteAsync(catalog);

        Assert.True(viewModel.IsStatusError);
        Assert.Equal("Season 3", Assert.Single(viewModel.Catalogs).Name);
    }

    [Fact]
    public async Task CancelRename_DiscardsEditsWithoutPersisting()
    {
        var viewModel = await TrySetUpAsync();
        if (viewModel is null)
        {
            return;
        }

        viewModel.NewCatalogName = "Season 3";
        await viewModel.CreateCommand.ExecuteAsync(null);
        var catalog = Assert.Single(viewModel.Catalogs);

        viewModel.BeginRenameCommand.Execute(catalog);
        catalog.EditName = "Something else entirely";
        viewModel.CancelRenameCommand.Execute(catalog);

        Assert.False(catalog.IsEditing);
        Assert.Equal("Season 3", catalog.Name);
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
    }
}
