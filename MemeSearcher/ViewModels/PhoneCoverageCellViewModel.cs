namespace MemeSearcher.ViewModels;

/// <summary>
/// One query-phoneme position in a coverage strip (#25). Deliberately four states, not a flattened
/// covered/uncovered boolean - the issue's own warning is that treating a substituted phone as
/// equivalent to an exact one is how a bad match looks good, and a query phoneme this candidate
/// lacks entirely reads differently depending on whether it sits inside the match's covered span
/// (a gap in an otherwise-covered stretch) or outside it (simply not covered at all).
/// </summary>
public enum PhoneCoverageState
{
    /// <summary>Not part of this match's covered span - the query asked for it, this candidate has nothing corresponding to it, and it's outside [QueryStart, QueryEnd).</summary>
    OutsideSpan,

    /// <summary>Exactly matched: the same symbol on both sides.</summary>
    Match,

    /// <summary>Aligned to a candidate phoneme, but a different symbol - the two are related enough to have scored an acceptable substitution cost, not identical.</summary>
    Substitute,

    /// <summary>Inside the covered span, but this specific query phoneme has no candidate counterpart at all - a hole in an otherwise-covered stretch, not an exact or substituted match.</summary>
    GapWithinSpan,
}

/// <summary>One rendered cell of a coverage strip: a query phoneme's symbol plus its state for this particular result.</summary>
public class PhoneCoverageCellViewModel(string symbol, PhoneCoverageState state)
{
    public string Symbol { get; } = symbol;

    public PhoneCoverageState State { get; } = state;

    public bool IsMatch { get; } = state == PhoneCoverageState.Match;

    public bool IsSubstitute { get; } = state == PhoneCoverageState.Substitute;

    public bool IsGap { get; } = state == PhoneCoverageState.GapWithinSpan;

    public bool IsOutsideSpan { get; } = state == PhoneCoverageState.OutsideSpan;
}
