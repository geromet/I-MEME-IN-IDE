namespace MemeSearcher.Core.Interfaces;

/// <summary>
/// Addendum §34's "query representation cache": phonemizing the same search query text repeatedly
/// (e.g. re-running a recent search, or single-source vs composite search both phonemizing the
/// same query text on the same request) is wasted work - this caches PhonemizeAsync's result keyed
/// on (text, language). Deliberately separate from any result cache (which the addendum marks
/// optional and explicitly not load-bearing for correctness) - this cache only ever short-circuits
/// phonemization, never the actual search/matching.
/// </summary>
public interface IQueryPhonemizationCache
{
    Task<PhonemizationResult> GetOrAddAsync(
        string queryText,
        string language,
        Func<CancellationToken, Task<PhonemizationResult>> factory,
        CancellationToken cancellationToken = default);
}
