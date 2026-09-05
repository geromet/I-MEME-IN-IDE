using System.Text.Json;
using MemeSearcher.Core.Search;
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

public sealed class TemplateAuthoringViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-template-authoring-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SearchOptionsAndVariantEdits_PersistThroughExistingTemplateModel()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, templateService) = setup.Value;
        viewModel.NewTemplateName = "Editable";
        await viewModel.CreateCommand.ExecuteAsync(null);
        var template = Assert.Single(viewModel.Templates);
        viewModel.SelectedTemplate = template;

        template.EditInsertionCost = "0.25";
        template.EditDeletionCost = "0.75";
        template.EditSubstitutionMaxCost = "0.6";
        template.EditWordBoundaryCost = "0.03";
        template.EditMinimumScore = "0.82";
        template.EditMaxResults = "17";
        await viewModel.SaveSearchOptionsCommand.ExecuteAsync(template);

        var saved = Assert.Single(await templateService.GetAllAsync());
        Assert.False(string.IsNullOrWhiteSpace(saved.SearchOptionsJson));
        var options = JsonSerializer.Deserialize<PhoneticSearchOptions>(saved.SearchOptionsJson!);
        Assert.NotNull(options);
        Assert.Equal(0.25, options.InsertionCost);
        Assert.Equal(0.75, options.DeletionCost);
        Assert.Equal(0.6, options.SubstitutionMaxCost);
        Assert.Equal(0.03, options.WordBoundaryCost);
        Assert.Equal(0.82, options.MinimumScore);
        Assert.Equal(17, options.MaxResults);

        viewModel.NewVariantLabel = "Before";
        viewModel.NewVariantPhonesRaw = "ʁ ɣ ʁ";
        await viewModel.AddVariantCommand.ExecuteAsync(null);
        var variant = Assert.Single(viewModel.Variants);
        viewModel.BeginVariantEditCommand.Execute(variant);
        variant.EditLabel = "After";
        variant.EditPhonesRaw = "ʁ ɣ";
        await viewModel.SaveVariantEditCommand.ExecuteAsync(variant);

        var edited = Assert.Single((await templateService.GetAllAsync()).Single().Variants);
        Assert.Equal("After", edited.Label);
        Assert.Equal("ʁ ɣ", edited.PhonesRaw);

        await viewModel.ResetSearchOptionsCommand.ExecuteAsync(template);
        Assert.Null((await templateService.GetAllAsync()).Single().SearchOptionsJson);
    }

    [Fact]
    public async Task InvalidOptionOrUnknownPhone_IsRejectedWithoutOverwritingPersistedState()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (viewModel, templateService) = setup.Value;
        viewModel.NewTemplateName = "Guarded";
        await viewModel.CreateCommand.ExecuteAsync(null);
        var template = Assert.Single(viewModel.Templates);
        viewModel.SelectedTemplate = template;

        template.EditMinimumScore = "1.5";
        await viewModel.SaveSearchOptionsCommand.ExecuteAsync(template);
        Assert.True(viewModel.IsStatusError);
        Assert.Null((await templateService.GetAllAsync()).Single().SearchOptionsJson);

        viewModel.NewVariantLabel = "Original";
        viewModel.NewVariantPhonesRaw = "ʁ ɣ ʁ";
        await viewModel.AddVariantCommand.ExecuteAsync(null);
        var variant = Assert.Single(viewModel.Variants);
        viewModel.BeginVariantEditCommand.Execute(variant);
        variant.EditLabel = "Should not persist";
        variant.EditPhonesRaw = "definitely-not-a-phone";
        await viewModel.SaveVariantEditCommand.ExecuteAsync(variant);

        Assert.True(viewModel.IsStatusError);
        var persisted = Assert.Single((await templateService.GetAllAsync()).Single().Variants);
        Assert.Equal("Original", persisted.Label);
        Assert.Equal("ʁ ɣ ʁ", persisted.PhonesRaw);
    }

    private async Task<(TemplatesViewModel ViewModel, TemplateService TemplateService)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return null;
        }

        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var templateService = new TemplateService(factory);
        var catalogService = new CatalogService(factory);
        var phonemizer = new EspeakPhonemizer(locator);
        var viewModel = new TemplatesViewModel(
            templateService,
            new TemplateSearchService(factory, new PhoneticSearchService(factory, phonemizer, new InMemoryQueryPhonemizationCache()), catalogService),
            new TemplateImportExportService(factory),
            catalogService,
            new LibraryService(factory),
            new SearchHistoryService(factory),
            new FakeMediaPlayerLauncher(),
            new FakeClipboardService(),
            new FFmpegClipExtractor(new FFmpegToolLocator()),
            new FakeFilePickerService());

        return (viewModel, templateService);
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
