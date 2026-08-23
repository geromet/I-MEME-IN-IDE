namespace MemeSearcher.Core.Search;

/// <summary>
/// One clip's contribution to a composite match: which media it came from, its timestamp range,
/// and which slice of the query it covers (addendum §22: composite results should show coverage,
/// e.g. "File A: a long / File B: bus" as bars under the query).
/// </summary>
public record CompositeMatchComponent(
    Guid MediaId,
    double StartSeconds,
    double EndSeconds,
    string SourceText,
    string Ipa,
    IReadOnlyList<string> Phonemes,
    int QueryStart,
    int QueryEnd,
    double Score);

/// <summary>
/// A match assembled from multiple source files (addendum §15-21) - kept as a separate type from
/// SearchResult rather than a shared base class, per addendum §21's own instruction not to
/// speculatively unify them.
/// </summary>
public record CompositeSearchResult(
    double OverallScore,
    IReadOnlyList<CompositeMatchComponent> Components,
    IReadOnlyList<string> QueryPhonemes);
