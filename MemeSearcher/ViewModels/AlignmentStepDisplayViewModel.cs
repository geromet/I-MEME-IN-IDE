using MemeSearcher.Core.Search;

namespace MemeSearcher.ViewModels;

/// <summary>
/// One query-to-match alignment step, rendered so a substitution or a missing/extra phoneme is
/// visibly distinct from an exact match (#15).
/// </summary>
public class AlignmentStepDisplayViewModel(QueryAlignmentStep step)
{
    public AlignmentOp Op { get; } = step.Op;

    public bool IsMatch { get; } = step.Op == AlignmentOp.Match;

    public bool IsProblem { get; } = step.Op != AlignmentOp.Match;

    public string Display { get; } = step.Op switch
    {
        AlignmentOp.Match => step.QuerySymbol ?? "",
        AlignmentOp.Substitute => $"{step.QuerySymbol}→{step.MatchSymbol}",
        // The query asked for this phoneme but the match doesn't have it.
        AlignmentOp.QueryExtra => $"{step.QuerySymbol}∅",
        // The match has this phoneme but the query didn't ask for it.
        AlignmentOp.CandidateExtra => $"+{step.MatchSymbol}",
        _ => "?",
    };
}
