using MemeSearcher.Core.Phonetics;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Templates;

/// <summary>Milestone 18 (#21) exit criterion: "templates survive restart; export then import round-trips."</summary>
public class TemplateImportExportServiceTests : IDisposable
{
    private readonly string _dbPathA = Path.Combine(Path.GetTempPath(), $"memesearcher-tmplexport-a-{Guid.NewGuid():N}.db");
    private readonly string _dbPathB = Path.Combine(Path.GetTempPath(), $"memesearcher-tmplexport-b-{Guid.NewGuid():N}.db");
    private readonly string _exportFilePath = Path.Combine(Path.GetTempPath(), $"memesearcher-tmplexport-{Guid.NewGuid():N}.json");

    private static async Task<IDbContextFactory<MemeSearcherDbContext>> NewDatabaseAsync(string dbPath)
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return factory;
    }

    [Fact]
    public async Task ExportThenImport_IntoTheSameDatabase_RecreatesTheTemplateAsANewRow()
    {
        var factory = await NewDatabaseAsync(_dbPathA);
        var templateService = new TemplateService(factory);
        var exportService = new TemplateImportExportService(factory);

        var originalId = await templateService.CreateAsync("Growl", "A specific animal noise");
        await templateService.AddVariantAsync(originalId, "US", "ʁ ɣ ʁ", PhoneAlphabet.Ipa);
        await templateService.AddVariantAsync(originalId, "UK", "ɣ ʁ ɣ", PhoneAlphabet.Ipa);

        var json = await exportService.ExportAsync(originalId);
        var newIds = await exportService.ImportAsync(json);

        var newId = Assert.Single(newIds);
        Assert.NotEqual(originalId, newId);

        var all = await templateService.GetAllAsync();
        Assert.Equal(2, all.Count);

        var imported = all.Single(t => t.Id == newId);
        Assert.Equal("Growl", imported.Name);
        Assert.Equal("A specific animal noise", imported.Description);
        Assert.Equal(2, imported.Variants.Count);
        Assert.Equal(["US", "UK"], imported.Variants.Select(v => v.Label));
        Assert.Equal(["ʁ ɣ ʁ", "ɣ ʁ ɣ"], imported.Variants.Select(v => v.PhonesRaw));
        Assert.All(imported.Variants, v => Assert.Equal(PhoneAlphabet.Ipa, v.Alphabet));
    }

    [Fact]
    public async Task ExportThenImport_ThroughARealFileIntoADifferentDatabase_RoundTrips()
    {
        var factoryA = await NewDatabaseAsync(_dbPathA);
        var templateServiceA = new TemplateService(factoryA);
        var exportServiceA = new TemplateImportExportService(factoryA);

        var originalId = await templateServiceA.CreateAsync("Catchphrase", null);
        await templateServiceA.AddVariantAsync(originalId, "As said", "h ɛ l oʊ", PhoneAlphabet.Ipa);

        var json = await exportServiceA.ExportAsync(originalId);
        await File.WriteAllTextAsync(_exportFilePath, json);

        // A genuinely separate database - proves the file, not the in-memory object, carries
        // everything needed (no shared catalog ids, no shared media - a real sharing scenario).
        var factoryB = await NewDatabaseAsync(_dbPathB);
        var templateServiceB = new TemplateService(factoryB);
        var exportServiceB = new TemplateImportExportService(factoryB);

        var jsonFromDisk = await File.ReadAllTextAsync(_exportFilePath);
        var importedIds = await exportServiceB.ImportAsync(jsonFromDisk);

        var importedId = Assert.Single(importedIds);
        var imported = Assert.Single(await templateServiceB.GetAllAsync());
        Assert.Equal(importedId, imported.Id);
        Assert.Equal("Catchphrase", imported.Name);
        var variant = Assert.Single(imported.Variants);
        Assert.Equal("h ɛ l oʊ", variant.PhonesRaw);
    }

    [Fact]
    public async Task Import_NeverPointsTheNewTemplateAtTheOriginalsTargetCatalog()
    {
        var factory = await NewDatabaseAsync(_dbPathA);
        var templateService = new TemplateService(factory);
        var catalogService = new MemeSearcher.Infrastructure.Catalogs.CatalogService(factory);
        var exportService = new TemplateImportExportService(factory);

        var catalogId = await catalogService.CreateAsync("Season 3", null);
        var originalId = await templateService.CreateAsync("Catchphrase", null);
        await templateService.AddVariantAsync(originalId, "As said", "h ɛ l oʊ", PhoneAlphabet.Ipa);
        await templateService.SetTargetCatalogAsync(originalId, catalogId);

        var json = await exportService.ExportAsync(originalId);
        var newId = Assert.Single(await exportService.ImportAsync(json));

        var imported = (await templateService.GetAllAsync()).Single(t => t.Id == newId);
        Assert.Null(imported.TargetCatalogId);
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPathA, _dbPathB, _exportFilePath })
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
