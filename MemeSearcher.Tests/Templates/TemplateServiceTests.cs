using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Templates;

/// <summary>Milestone 18 (#21): template/variant CRUD, and the target-catalog SetNull behavior a deleted catalog must leave behind.</summary>
public class TemplateServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-templatesvc-test-{Guid.NewGuid():N}.db");

    private async Task<(TemplateService Templates, CatalogService Catalogs)> SetUpAsync()
    {
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await dbContextFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        return (new TemplateService(dbContextFactory), new CatalogService(dbContextFactory));
    }

    [Fact]
    public async Task CreateAsync_NewTemplate_HasNoVariantsAndSurvivesAFreshServiceInstance()
    {
        var (templates, _) = await SetUpAsync();

        var id = await templates.CreateAsync("Screams", "Nonverbal sounds");

        var reopened = new TemplateService(new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>());

        var summary = Assert.Single(await reopened.GetAllAsync());
        Assert.Equal(id, summary.Id);
        Assert.Equal("Screams", summary.Name);
        Assert.Equal("Nonverbal sounds", summary.Description);
        Assert.Empty(summary.Variants);
        Assert.Equal(SearchMode.SimilarPhonetic, summary.Mode);
    }

    [Fact]
    public async Task AddVariantAsync_MultipleVariants_AreOrderedBySequence()
    {
        var (templates, _) = await SetUpAsync();
        var templateId = await templates.CreateAsync("Laugh", null);

        await templates.AddVariantAsync(templateId, "US", "h æ h æ", PhoneAlphabet.Ipa);
        await templates.AddVariantAsync(templateId, "UK", "h ɑ h ɑ", PhoneAlphabet.Ipa);

        var summary = Assert.Single(await templates.GetAllAsync());
        Assert.Equal(2, summary.Variants.Count);
        Assert.Equal("US", summary.Variants[0].Label);
        Assert.Equal("UK", summary.Variants[1].Label);
    }

    [Fact]
    public async Task RemoveVariantAsync_RemovesOnlyThatVariant()
    {
        var (templates, _) = await SetUpAsync();
        var templateId = await templates.CreateAsync("Laugh", null);
        var keepId = await templates.AddVariantAsync(templateId, "US", "h æ h æ", PhoneAlphabet.Ipa);
        var removeId = await templates.AddVariantAsync(templateId, "UK", "h ɑ h ɑ", PhoneAlphabet.Ipa);

        await templates.RemoveVariantAsync(removeId);

        var summary = Assert.Single(await templates.GetAllAsync());
        var variant = Assert.Single(summary.Variants);
        Assert.Equal(keepId, variant.Id);
    }

    [Fact]
    public async Task DeletingATemplate_CascadeDeletesItsVariants()
    {
        var (templates, _) = await SetUpAsync();
        var templateId = await templates.CreateAsync("Laugh", null);
        await templates.AddVariantAsync(templateId, "US", "h æ h æ", PhoneAlphabet.Ipa);

        await templates.DeleteAsync(templateId);

        Assert.Empty(await templates.GetAllAsync());
    }

    [Fact]
    public async Task DeletingTheTargetCatalog_ClearsTheTemplatesTargetCatalog_WithoutDeletingTheTemplate()
    {
        var (templates, catalogs) = await SetUpAsync();
        var catalogId = await catalogs.CreateAsync("Season 3", null);
        var templateId = await templates.CreateAsync("Catchphrase", null);
        await templates.SetTargetCatalogAsync(templateId, catalogId);

        var beforeDelete = Assert.Single(await templates.GetAllAsync());
        Assert.Equal(catalogId, beforeDelete.TargetCatalogId);

        await catalogs.DeleteAsync(catalogId);

        var afterDelete = Assert.Single(await templates.GetAllAsync());
        Assert.Equal(templateId, afterDelete.Id);
        Assert.Null(afterDelete.TargetCatalogId);
    }

    [Fact]
    public async Task DeletingATemplate_ClearsTemplateIdOnItsHistoryEntries_WithoutDeletingThem()
    {
        var (templates, _) = await SetUpAsync();
        var templateId = await templates.CreateAsync("Growl", null);

        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();
        var historyService = new SearchHistoryService(dbContextFactory);
        await historyService.RecordTemplateRunAsync(templateId, "Growl", "All indexed media", resultCount: 1);

        await templates.DeleteAsync(templateId);

        // Not GetRecentTemplateRunsAsync - that filters on TemplateId != null, which this row no
        // longer satisfies after SetNull. Reading the table directly is the only way to see the
        // "cleared but not deleted" row this test exists to prove.
        await using var context = await dbContextFactory.CreateDbContextAsync();
        var entry = Assert.Single(await context.SearchHistory.ToListAsync());
        Assert.Null(entry.TemplateId);
        Assert.Equal("Growl", entry.TemplateName);
        Assert.Equal(1, entry.ResultCount);
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
