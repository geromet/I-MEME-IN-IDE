namespace MemeSearcher.Core.Search;

/// <summary>
/// Timing is nullable because a match can come from a transcript that never had any (#32). A
/// result without timing cannot be played, clipped, or timestamped, and must not pretend to be at
/// zero seconds.
/// </summary>
public record SearchResult(
    Guid MediaId,
    double? StartSeconds,
    double? EndSeconds,
    string SourceText,
    string Ipa,
    IReadOnlyList<string> MatchPhonemes,
    IReadOnlyList<string> QueryPhonemes,
    double Score);
