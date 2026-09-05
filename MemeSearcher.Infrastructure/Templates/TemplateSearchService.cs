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

/// <summary>#36 composite counterpart to <see cref="TemplateSearchOutcome"/>.</summary>
public record TemplateCompositeSearchOutcome(
    IReadOnlyList<CompositeSearchResult> Results,
    string ScopeDescription,
    IReadOnlyCollection<Guid>? SelectedMediaIds);

/// <summary>
/// Runs a Template against the corpus (#21/#36). A template matches if ANY of its variants match
/// (handoff §33). Both single-source and composite execution use the template's hand-authored phone
/// tokens directly, bypassing the phonemizer/query cache, and share the same persisted search
/// options and one resolved search scope.
/// </summary>
public class TemplateSearchService(
    IDbContextFactory<MemeSearcherDbContext> dbContextFactory,
    IPhoneticSearchService searchService,
    CatalogService catalogService,
    ICompositeSearchService? compositeSearchService = null)
{
    private sealed record TemplateSearchRequest(
        IReadOnlyList<TemplateVariant> Variants,
        SearchScope Scope,
        string ScopeDescription,
        IReadOnlyCollection<Guid>? SelectedMediaIds,
        SearchMode Mode,
        PhoneticSearchOptions Options);

    public async Task<TemplateSearchOutcome> SearchAsync(
        Guid templateId, SearchScope? scopeOverride = null, CancellationToken cancellationToken = default)
    {
        var request = await BuildRequestAsync(templateId, scopeOverride, cancellationToken);
        if (request.Variants.Count == 0)
        {
            return new TemplateSearchOutcome([], request.ScopeDescription, request.SelectedMediaIds);
        }

        var perVariantResults = await Task.WhenAll(request.Variants.Select(variant =>
        {
            var tokens = TemplatePhoneParser.BuildTokens(variant.PhonesRaw, variant.Alphabet);
            return searchService.SearchAsync(tokens, request.Scope, request.Mode, request.Options, cancellationToken);
        }));

        var results = perVariantResults
            .SelectMany(r => r)
            .GroupBy(r => (r.MediaId, r.StartSeconds, r.EndSeconds))
            .Select(g => g.OrderByDescending(r => r.Score).First())
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.MediaId)
            .ThenBy(r => r.StartSeconds)
            .Take(request.Options.MaxResults)
            .ToList();

        return new TemplateSearchOutcome(results, request.ScopeDescription, request.SelectedMediaIds);
    }

    /// <summary>
    /// #36: runs the same authored variants/options/scope through multi-file composite matching.
    /// The composite service's phone-token overload is required so a deliberately authored sound
    /// is never round-tripped through text or phonemized a second time.
    /// </summary>
    public async Task<TemplateCompositeSearchOutcome> SearchCompositeAsync(
        Guid templateId, SearchScope? scopeOverride = null, CancellationToken cancellationToken = default)
    {
        if (compositeSearchService is null)
        {
            throw new InvalidOperationException("Composite template search is not configured.");
        }

        var request = await BuildRequestAsync(templateId, scopeOverride, cancellationToken);
        if (request.Variants.Count == 0)
        {
            return new TemplateCompositeSearchOutcome([], request.ScopeDescription, request.SelectedMediaIds);
        }

        var perVariantResults = await Task.WhenAll(request.Variants.Select(variant =>
        {
            var tokens = TemplatePhoneParser.BuildTokens(variant.PhonesRaw, variant.Alphabet);
            return compositeSearchService.SearchAsync(tokens, request.Scope, request.Options, cancellationToken);
        }));

        var results = perVariantResults
            .SelectMany(r => r)
            .GroupBy(CompositeSignature)
            .Select(g => g.OrderByDescending(r => r.OverallScore).First())
            .OrderByDescending(r => r.OverallScore)
            .ThenBy(r => r.Components.Count)
            .Take(request.Options.MaxResults)
            .ToList();

        return new TemplateCompositeSearchOutcome(results, request.ScopeDescription, request.SelectedMediaIds);
    }

    private async Task<TemplateSearchRequest> BuildRequestAsync(
        Guid templateId, SearchScope? scopeOverride, CancellationToken cancellationToken)
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

        var modeDefaults = PhoneticSearchOptions.ForMode(template.Mode);
        var options = string.IsNullOrEmpty(template.SearchOptionsJson)
            ? modeDefaults
            : JsonSerializer.Deserialize<PhoneticSearchOptions>(template.SearchOptionsJson) ?? modeDefaults;

        return new TemplateSearchRequest(
            variants, scope, scopeDescription, selectedMediaIds, template.Mode, options);
    }

    private static string CompositeSignature(CompositeSearchResult result) =>
        string.Join('|', result.Components.Select(component =>
            $"{component.MediaId}:{component.StartSeconds}:{component.EndSeconds}"));

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
