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

    /// <summary>
    /// Milestone 18 (#21): searches a query given directly as phone tokens, bypassing the
    /// phonemizer and the #7 query cache entirely - the entry point a Template's hand-authored
    /// phones use, since they express sounds espeak was never asked to predict (a laugh, a
    /// stutter, a mispronunciation authored on purpose). Shares every downstream step (n-gram
    /// candidate generation, the DP matcher) with the text overload above, which now builds
    /// tokens and calls this.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        IReadOnlyList<PhoneToken> queryTokens,
        SearchScope scope,
        SearchMode mode = SearchMode.SimilarPhonetic,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default);
}
