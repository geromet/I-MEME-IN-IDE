using MemeSearcher.Core.Jobs;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Templates;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Templates;

/// <summary>Exercises TemplatesViewModel's export/import commands end to end through a real temp file, using FakeFilePickerService to stand in for the OS file dialog.</summary>
public class TemplatesViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-templatesvm-test-{Guid.NewGuid():N}.db");
    private readonly string _exportPath = Path.Combine(Path.GetTempPath(), $"memesearcher-templatesvm-export-{Guid.NewGuid():N}.json");

    private async Task<(TemplatesViewModel ViewModel, FakeFilePickerService FilePicker, TemplateService TemplateService)?> TrySetUpAsync()
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

        var phonemizer = new EspeakPhonemizer(locator);
        var libraryService = new LibraryService(dbContextFactory);
        var catalogService = new CatalogService(dbContextFactory);
        var templateService = new TemplateService(dbContextFactory);
        var filePicker = new FakeFilePickerService();

        var viewModel = new TemplatesViewModel(
            templateService,
            new TemplateSearchService(dbContextFactory, new PhoneticSearchService(dbContextFactory, phonemizer, new InMemoryQueryPhonemizationCache()), catalogService),
            new TemplateImportExportService(dbContextFactory),
            catalogService,
            libraryService,
            new SearchHistoryService(dbContextFactory),
            new FakeMediaPlayerLauncher(),
            new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()),
            filePicker);

        return (viewModel, filePicker, templateService);
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsThroughARealFile()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, filePicker, templateService) = setup.Value;

        viewModel.NewTemplateName = "Growl";
        await viewModel.CreateCommand.ExecuteAsync(null);
        var template = Assert.Single(viewModel.Templates);
        viewModel.SelectedTemplate = template;

        viewModel.NewVariantPhonesRaw = "ʁ ɣ ʁ";
        await viewModel.AddVariantCommand.ExecuteAsync(null);

        filePicker.TemplateExportPathToReturn = _exportPath;
        await viewModel.ExportCommand.ExecuteAsync(template);

        // System.Text.Json escapes non-ASCII by default, so the raw file won't contain "ʁ" as a
        // literal byte sequence - the real round-trip proof is the re-imported variant's phones
        // matching below, not a substring check on the escaped JSON.
        Assert.True(File.Exists(_exportPath));
        Assert.Contains("Growl", await File.ReadAllTextAsync(_exportPath));

        filePicker.TemplateImportPathToReturn = _exportPath;
        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Templates.Count);
        var importedTemplate = Assert.Single(viewModel.Templates, t => t.Id != template.Id);
        Assert.Equal("Growl", importedTemplate.Name);
        Assert.Contains("Imported 1 template", viewModel.StatusMessage);

        var importedVariant = Assert.Single(await templateService.GetAllAsync(), t => t.Id == importedTemplate.Id);
        Assert.Equal("ʁ ɣ ʁ", Assert.Single(importedVariant.Variants).PhonesRaw);
    }

    [Fact]
    public async Task RunningATemplate_RecordsItInRecentRuns_AndRerunCommandRunsItAgain()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, _, _) = setup.Value;

        viewModel.NewTemplateName = "Growl";
        await viewModel.CreateCommand.ExecuteAsync(null);
        var template = Assert.Single(viewModel.Templates);
        viewModel.SelectedTemplate = template;

        viewModel.NewVariantPhonesRaw = "ʁ ɣ ʁ";
        await viewModel.AddVariantCommand.ExecuteAsync(null);

        await viewModel.RunCommand.ExecuteAsync(template);

        var run = Assert.Single(viewModel.RecentRuns);
        Assert.Equal(template.Id, run.TemplateId);
        Assert.Equal("Growl", run.TemplateName);
        Assert.Equal("All indexed media", run.ScopeDescription);
        Assert.Null(run.QueryText);

        // Re-running looks the template up by TemplateId, not by any reconstructed data on the
        // history entry, and records a second run.
        await viewModel.RerunCommand.ExecuteAsync(run);

        Assert.Equal(2, viewModel.RecentRuns.Count);
        Assert.All(viewModel.RecentRuns, r => Assert.Equal(template.Id, r.TemplateId));
    }

    [Fact]
    public async Task RerunCommand_WhenTheTemplateNoLongerExists_ReportsAnErrorInsteadOfThrowing()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, _, _) = setup.Value;
        var staleEntry = new MemeSearcher.Core.Models.SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            TemplateId = null, // as it would be after the template's own deletion (SetNull)
            TemplateName = "Deleted Template",
            ScopeDescription = "All indexed media",
            ResultCount = 0,
            SearchedAt = DateTimeOffset.UtcNow,
        };

        await viewModel.RerunCommand.ExecuteAsync(staleEntry);

        Assert.True(viewModel.IsStatusError);
        Assert.Contains("Deleted Template", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPath, _exportPath })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
