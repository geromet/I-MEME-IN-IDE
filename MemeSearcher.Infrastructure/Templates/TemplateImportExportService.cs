using System.Text.Json;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Templates;

/// <summary>Serializes templates to/from the plain JSON file format in TemplateExportFile (#21), so a template can be shared as a file rather than only living in one database.</summary>
public class TemplateImportExportService(IDbContextFactory<MemeSearcherDbContext> dbContextFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<string> ExportAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        await ExportAsync([templateId], cancellationToken);

    public async Task<string> ExportAsync(IReadOnlyCollection<Guid> templateIds, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var templates = await context.Templates
            .Where(t => templateIds.Contains(t.Id))
            .ToListAsync(cancellationToken);
        var variants = await context.TemplateVariants
            .Where(v => templateIds.Contains(v.TemplateId))
            .ToListAsync(cancellationToken);
        var variantsByTemplate = variants.ToLookup(v => v.TemplateId);

        var entries = templates.Select(t => new TemplateExportEntry(
            t.Name,
            t.Description,
            t.Mode.ToString(),
            t.SearchOptionsJson,
            variantsByTemplate[t.Id]
                .OrderBy(v => v.Sequence)
                .Select(v => new TemplateExportVariant(v.Label, v.PhonesRaw, v.Alphabet.ToString()))
                .ToList())).ToList();

        return JsonSerializer.Serialize(new TemplateExportFile(entries), JsonOptions);
    }

    /// <summary>
    /// Always creates new templates with fresh ids - an import is never an update to an existing
    /// template, even one with the same name, since two people's templates named "Laugh" are not
    /// the same template. Returns the new templates' ids.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        var file = JsonSerializer.Deserialize<TemplateExportFile>(json)
            ?? throw new InvalidOperationException("Not a recognisable template file.");

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var newIds = new List<Guid>();

        foreach (var entry in file.Templates)
        {
            var template = new Template
            {
                Id = Guid.NewGuid(),
                Name = entry.Name,
                Description = entry.Description,
                Mode = Enum.Parse<SearchMode>(entry.Mode),
                SearchOptionsJson = entry.SearchOptionsJson,
                TargetCatalogId = null,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Templates.Add(template);
            newIds.Add(template.Id);

            for (var i = 0; i < entry.Variants.Count; i++)
            {
                var variant = entry.Variants[i];
                context.TemplateVariants.Add(new TemplateVariant
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    Label = variant.Label,
                    PhonesRaw = variant.PhonesRaw,
                    Alphabet = Enum.Parse<PhoneAlphabet>(variant.Alphabet),
                    Sequence = i,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return newIds;
    }
}
