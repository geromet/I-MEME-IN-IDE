namespace MemeSearcher.Core.Search;

/// <summary>
/// One matched phoneme with its own timing and provenance (#15). Timing is nullable for the same
/// reason as SearchResult's own StartSeconds/EndSeconds (#32); IsPhoneLevelAligned is false when
/// this phoneme's span was inherited from its whole word rather than measured per-phone by an
/// alignment provider (see PhoneStreamBuilder's own doc comment) - the inspector's "aligned" vs
/// "estimated" distinction (handoff §49) comes directly from this flag.
/// </summary>
public record MatchedPhone(string Symbol, double? StartSeconds, double? EndSeconds, bool IsPhoneLevelAligned);

/// <summary>
/// One step of the query-to-match alignment, resolved to symbols rather than raw indices so the
/// inspector doesn't need to reach back into either token list (#15). QuerySymbol is null for a
/// CandidateExtra step (the match has a phoneme the query didn't ask for); MatchSymbol is null for
/// a QueryExtra step (the query asked for a phoneme this match doesn't have).
/// </summary>
public record QueryAlignmentStep(AlignmentOp Op, string? QuerySymbol, string? MatchSymbol);

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
    double Score,
    IReadOnlyList<MatchedPhone>? MatchedPhoneDetails = null,
    IReadOnlyList<QueryAlignmentStep>? AlignmentSteps = null)
{
    public IReadOnlyList<MatchedPhone> MatchedPhoneDetails { get; init; } = MatchedPhoneDetails ?? [];

    public IReadOnlyList<QueryAlignmentStep> AlignmentSteps { get; init; } = AlignmentSteps ?? [];
}
