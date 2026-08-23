namespace MemeSearcher.Core.Search;

public record SearchResult(
    Guid MediaId,
    double StartSeconds,
    double EndSeconds,
    string SourceText,
    string Ipa,
    IReadOnlyList<string> MatchPhonemes,
    IReadOnlyList<string> QueryPhonemes,
    double Score);
