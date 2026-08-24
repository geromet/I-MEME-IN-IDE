namespace MemeSearcher.Core.Search;

/// <summary>
/// One clip's contribution to a composite match: the MatchComponent shape (#17) plus which slice
/// of the query it covers (addendum §22: composite results should show coverage, e.g.
/// "File A: a long / File B: bus" as bars under the query) - QueryStart/QueryEnd have no analog on
/// plain SearchResult, since a single-source match implicitly covers the query end to end.
/// </summary>
public record CompositeMatchComponent(
    Guid MediaId,
    double? StartSeconds,
    double? EndSeconds,
    string SourceText,
    string Ipa,
    IReadOnlyList<string> Phonemes,
    double Score,
    int QueryStart,
    int QueryEnd)
    : MatchComponent(MediaId, StartSeconds, EndSeconds, SourceText, Ipa, Phonemes, Score);

/// <summary>
/// A match assembled from multiple source files (addendum §15-21) - kept as a separate type from
/// SearchResult rather than a shared base class, per addendum §21's own instruction not to
/// speculatively unify them. The two only share the per-clip MatchComponent shape (#17), one level
/// down, not this top-level wrapper.
/// </summary>
public record CompositeSearchResult(
    double OverallScore,
    IReadOnlyList<CompositeMatchComponent> Components,
    IReadOnlyList<string> QueryPhonemes);
