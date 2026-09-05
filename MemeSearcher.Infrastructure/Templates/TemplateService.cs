using MemeSearcher.Core.Models;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Templates;

/// <summary>CRUD for templates and their variants (#21). Running a template against the corpus is TemplateSearchService's job, not this class's.</summary>
public class TemplateService(IDbContextFactory<MemeSearcherDbContext> dbContextFactory)
{
    public async Task<List<TemplateSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var templates = (await context.Templates.ToListAsync(cancellationToken))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
        var variants = await context.TemplateVariants.ToListAsync(cancellationToken);
        var variantsByTemplate = variants.ToLookup(v => v.TemplateId);

        return templates.Select(t => ToSummary(t, variantsByTemplate[t.Id])).ToList();
    }

    public async Task<Guid> CreateAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var template = new Template
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Mode = SearchMode.SimilarPhonetic,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);
        return template.Id;
    }

    public async Task RenameAsync(Guid templateId, string name, string? description, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var template = await context.Templates.FindAsync([templateId], cancellationToken);
        if (template is null)
        {
            return;
        }

        template.Name = name;
        template.Description = description;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetModeAsync(Guid templateId, SearchMode mode, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var template = await context.Templates.FindAsync([templateId], cancellationToken);
        if (template is null)
        {
            return;
        }

        template.Mode = mode;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// #36: persists explicitly authored matcher knobs as the existing SearchOptionsJson payload.
    /// Keeping serialization at this CRUD boundary means TemplateSearchService remains the single
    /// consumer and no parallel template-options model is introduced.
    /// </summary>
    public async Task SetSearchOptionsAsync(Guid templateId, string? searchOptionsJson, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var template = await context.Templates.FindAsync([templateId], cancellationToken);
        if (template is null)
        {
            return;
        }

        template.SearchOptionsJson = string.IsNullOrWhiteSpace(searchOptionsJson) ? null : searchOptionsJson;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTargetCatalogAsync(Guid templateId, Guid? catalogId, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var template = await context.Templates.FindAsync([templateId], cancellationToken);
        if (template is null)
        {
            return;
        }

        template.TargetCatalogId = catalogId;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var template = await context.Templates.FindAsync([templateId], cancellationToken);
        if (template is null)
        {
            return;
        }

        // TemplateVariant rows cascade-delete via the FK configured in MemeSearcherDbContext.
        context.Templates.Remove(template);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddVariantAsync(Guid templateId, string label, string phonesRaw, PhoneAlphabet alphabet, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var nextSequence = await context.TemplateVariants
            .Where(v => v.TemplateId == templateId)
            .Select(v => (int?)v.Sequence)
            .MaxAsync(cancellationToken) ?? -1;

        var variant = new TemplateVariant
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            Label = label,
            PhonesRaw = phonesRaw,
            Alphabet = alphabet,
            Sequence = nextSequence + 1,
        };

        context.TemplateVariants.Add(variant);
        await context.SaveChangesAsync(cancellationToken);
        return variant.Id;
    }

    public async Task UpdateVariantAsync(Guid variantId, string label, string phonesRaw, PhoneAlphabet alphabet, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var variant = await context.TemplateVariants.FindAsync([variantId], cancellationToken);
        if (variant is null)
        {
            return;
        }

        variant.Label = label;
        variant.PhonesRaw = phonesRaw;
        variant.Alphabet = alphabet;

        var template = await context.Templates.FindAsync([variant.TemplateId], cancellationToken);
        if (template is not null)
        {
            template.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveVariantAsync(Guid variantId, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var variant = await context.TemplateVariants.FindAsync([variantId], cancellationToken);
        if (variant is null)
        {
            return;
        }

        context.TemplateVariants.Remove(variant);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static TemplateSummary ToSummary(Template template, IEnumerable<TemplateVariant> variants) =>
        new(
            template.Id,
            template.Name,
            template.Description,
            template.Mode,
            template.TargetCatalogId,
            template.SearchOptionsJson,
            template.CreatedAt,
            variants
                .OrderBy(v => v.Sequence)
                .Select(v => new TemplateVariantSummary(v.Id, v.Label, v.PhonesRaw, v.Alphabet, v.Sequence))
                .ToList());
}
