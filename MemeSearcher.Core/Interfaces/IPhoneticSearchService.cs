using MemeSearcher.Core.Search;

namespace MemeSearcher.Core.Interfaces;

public interface IPhoneticSearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string queryText,
        string language,
        SearchScope scope,
        SearchMode mode = SearchMode.SimilarPhonetic,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default);
}
