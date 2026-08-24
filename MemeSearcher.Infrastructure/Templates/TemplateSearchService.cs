using System.Text.Json;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Templates;

/// <summary>
/// The scope a template run actually searched, alongside its results - callers that record the
/// run (TemplatesViewModel, for search history) describe *this*, rather than re-deriving the scope
/// themselves from the template's TargetCatalogId a second time. Two independent resolutions of
/// the same catalog membership can disagree if the catalog changes between them; one resolution
/// shared by both the search and its description cannot.
/// </summary>
public record TemplateSearchOutcome(
    IReadOnlyList<SearchResult> Results,
    string ScopeDescription,
    IReadOnlyCollection<Guid>? SelectedMediaIds);

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
    public async Task<TemplateSearchOutcome> SearchAsync(
        Guid templateId, SearchScope? scopeOverride = null, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var template = await context.Templates.FindAsync([templateId], cancellationToken)
            ?? throw new InvalidOperationException($"Template {templateId} no longer exists.");
        var variants = await context.TemplateVariants
            .Where(v => v.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        var (scope, scopeDescription, selectedMediaIds) = scopeOverride is not null
            ? DescribeExplicitScope(scopeOverride)
            : await ResolveDefaultScopeAsync(template.TargetCatalogId, context, cancellationToken);

        if (variants.Count == 0)
        {
            return new TemplateSearchOutcome([], scopeDescription, selectedMediaIds);
        }

        var options = string.IsNullOrEmpty(template.SearchOptionsJson)
            ? PhoneticSearchOptions.ForMode(template.Mode)
            : JsonSerializer.Deserialize<PhoneticSearchOptions>(template.SearchOptionsJson);

        var perVariantResults = await Task.WhenAll(variants.Select(variant =>
        {
            var tokens = TemplatePhoneParser.BuildTokens(variant.PhonesRaw, variant.Alphabet);
            return searchService.SearchAsync(tokens, scope, template.Mode, options, cancellationToken);
        }));

        var results = perVariantResults
            .SelectMany(r => r)
            .GroupBy(r => (r.MediaId, r.StartSeconds, r.EndSeconds))
            .Select(g => g.OrderByDescending(r => r.Score).First())
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.MediaId)
            .ThenBy(r => r.StartSeconds)
            .Take(options?.MaxResults ?? PhoneticSearchOptions.ForMode(template.Mode).MaxResults)
            .ToList();

        return new TemplateSearchOutcome(results, scopeDescription, selectedMediaIds);
    }

    private async Task<(SearchScope Scope, string Description, IReadOnlyCollection<Guid>? SelectedMediaIds)> ResolveDefaultScopeAsync(
        Guid? targetCatalogId, MemeSearcherDbContext context, CancellationToken cancellationToken)
    {
        if (targetCatalogId is null)
        {
            return (new SearchScope.AllIndexedMedia(), "All indexed media", null);
        }

        var memberIds = await catalogService.GetMemberIdsAsync(targetCatalogId.Value, cancellationToken);
        var catalog = await context.Catalogs.FindAsync([targetCatalogId.Value], cancellationToken);
        var catalogName = catalog?.Name ?? "deleted catalog";

        return (new SearchScope.SelectedMedia(memberIds), $"Catalog: {catalogName} ({memberIds.Count} source(s))", memberIds);
    }

    private static (SearchScope Scope, string Description, IReadOnlyCollection<Guid>? SelectedMediaIds) DescribeExplicitScope(SearchScope scope) =>
        scope is SearchScope.SelectedMedia selected
            ? (scope, $"{selected.MediaIds.Count} source(s)", selected.MediaIds)
            : (scope, "All indexed media", null);
}
