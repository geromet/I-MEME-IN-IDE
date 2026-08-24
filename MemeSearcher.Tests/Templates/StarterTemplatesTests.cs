using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Templates;

/// <summary>#21's shipped starter set: must actually import, and every phone must validate - a shipped template that trips the editor's own "unrecognised symbol" warning would be an embarrassing first impression.</summary>
public class StarterTemplatesTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-starter-{Guid.NewGuid():N}.db");

    [Fact]
    public void BuildExportJson_EveryVariantsPhones_AreAllRecognisedByThePhonemeFeatureTable()
    {
        var json = StarterTemplates.BuildExportJson();
        var file = System.Text.Json.JsonSerializer.Deserialize<TemplateExportFile>(json)!;

        Assert.NotEmpty(file.Templates);

        foreach (var template in file.Templates)
        {
            Assert.NotEmpty(template.Variants);

            foreach (var variant in template.Variants)
            {
                var alphabet = Enum.Parse<PhoneAlphabet>(variant.Alphabet);
                var parsed = TemplatePhoneParser.ParseSymbols(variant.PhonesRaw, alphabet);

                Assert.NotEmpty(parsed);
                Assert.All(parsed, p => Assert.True(p.IsKnown, $"'{p.AsAuthored}' in \"{template.Name}\"/\"{variant.Label}\" is not recognised."));
            }
        }
    }

    [Fact]
    public async Task LoadStarterTemplates_ImportsCleanlyIntoARealDatabase()
    {
        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await dbContextFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var importExportService = new TemplateImportExportService(dbContextFactory);
        var newIds = await importExportService.ImportAsync(StarterTemplates.BuildExportJson());

        var templateService = new TemplateService(dbContextFactory);
        var all = await templateService.GetAllAsync();

        Assert.Equal(newIds.Count, all.Count);
        Assert.All(all, t => Assert.NotEmpty(t.Variants));
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
