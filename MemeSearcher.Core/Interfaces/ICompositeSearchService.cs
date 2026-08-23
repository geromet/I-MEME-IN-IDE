using MemeSearcher.Core.Search;

namespace MemeSearcher.Core.Interfaces;

/// <summary>
/// Milestone 4: assembles a query out of clips from multiple different media items. Separate from
/// IPhoneticSearchService because the result shape is genuinely different (addendum §17: single-
/// source is the default and simpler mode; composite is explicitly opt-in).
/// </summary>
public interface ICompositeSearchService
{
    Task<IReadOnlyList<CompositeSearchResult>> SearchAsync(
        string queryText,
        string language,
        SearchScope scope,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default);
}
