using System.Text.Json;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Templates;

/// <summary>
/// Runs a Template against the corpus (#21). A template matches if ANY of its variants match
/// (handoff §33) - each variant is searched independently through
/// IPhoneticSearchService.SearchAsync's phone-token overload (bypassing the phonemizer and the #7
/// query cache, since a hand-authored phone sequence has no text to key either against), and the
/// per-variant result sets are unioned, deduplicated by (media, span), keeping the best score for
/// any span more than one variant happened to match.
/// </summary>
public class TemplateSearchService(
    IDbContextFactory<MemeSearcherDbContext> dbContextFactory,
    IPhoneticSearchService searchService,
    CatalogService catalogService)
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        Guid templateId, SearchScope? scopeOverride = null, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var template = await context.Templates.FindAsync([templateId], cancellationToken)
            ?? throw new InvalidOperationException($"Template {templateId} no longer exists.");
        var variants = await context.TemplateVariants
            .Where(v => v.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            return [];
        }

        var scope = scopeOverride ?? await ResolveDefaultScopeAsync(template.TargetCatalogId, cancellationToken);
        var options = string.IsNullOrEmpty(template.SearchOptionsJson)
            ? PhoneticSearchOptions.ForMode(template.Mode)
            : JsonSerializer.Deserialize<PhoneticSearchOptions>(template.SearchOptionsJson);

        var perVariantResults = await Task.WhenAll(variants.Select(variant =>
        {
            var tokens = TemplatePhoneParser.BuildTokens(variant.PhonesRaw, variant.Alphabet);
            return searchService.SearchAsync(tokens, scope, template.Mode, options, cancellationToken);
        }));

        return perVariantResults
            .SelectMany(results => results)
            .GroupBy(r => (r.MediaId, r.StartSeconds, r.EndSeconds))
            .Select(g => g.OrderByDescending(r => r.Score).First())
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.MediaId)
            .ThenBy(r => r.StartSeconds)
            .Take(options?.MaxResults ?? PhoneticSearchOptions.ForMode(template.Mode).MaxResults)
            .ToList();
    }

    private async Task<SearchScope> ResolveDefaultScopeAsync(Guid? targetCatalogId, CancellationToken cancellationToken)
    {
        if (targetCatalogId is null)
        {
            return new SearchScope.AllIndexedMedia();
        }

        var memberIds = await catalogService.GetMemberIdsAsync(targetCatalogId.Value, cancellationToken);
        return new SearchScope.SelectedMedia(memberIds);
    }
}
