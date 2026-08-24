namespace MemeSearcher.Core.Search;

/// <summary>
/// The per-clip data shared by every kind of match result (#17 - the "sharing only a
/// MatchComponent-shaped record" half of the original design notes that #4's composite search
/// shipped without): which media it came from, its timestamp range, its text/IPA rendering, the
/// phones actually matched, and a score. <see cref="SearchResult"/> and
/// <see cref="CompositeMatchComponent"/> both derive from this rather than independently
/// redeclaring the same seven fields - deliberately not a reason to unify SearchResult and
/// CompositeSearchResult themselves (addendum §21's instruction not to speculatively merge the two
/// top-level result types still holds; this is one level down, at the single-clip shape they were
/// each already built from).
///
/// Timing is nullable because a match can come from a transcript that never had any (#32). A
/// result without timing cannot be played, clipped, or timestamped, and must not pretend to be at
/// zero seconds.
/// </summary>
public record MatchComponent(
    Guid MediaId,
    double? StartSeconds,
    double? EndSeconds,
    string SourceText,
    string Ipa,
    IReadOnlyList<string> Phonemes,
    double Score);
