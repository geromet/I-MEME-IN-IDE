using System.Collections.Generic;
using System.Linq;
using MemeSearcher.Core.Search;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Builds the shared coverage-strip cell list (#25) from a SearchResult's query-side data. One
/// query phoneme becomes one cell, regardless of whether this particular match says anything about
/// it - so every strip for the same query is the same length and aligns against the same ruler.
/// </summary>
public static class PhoneCoverageStripBuilder
{
    public static IReadOnlyList<PhoneCoverageCellViewModel> Build(
        IReadOnlyList<string> queryPhonemes,
        IReadOnlyList<QueryAlignmentStep> alignmentSteps,
        int queryStart,
        int queryEnd)
    {
        var states = new PhoneCoverageState?[queryPhonemes.Count];

        foreach (var step in alignmentSteps)
        {
            // CandidateExtra doesn't consume a query position at all - nothing to place on this strip.
            if (step.QueryIndex is not int index || index < 0 || index >= states.Length)
            {
                continue;
            }

            var withinSpan = index >= queryStart && index < queryEnd;

            states[index] = step.Op switch
            {
                AlignmentOp.Match => PhoneCoverageState.Match,
                AlignmentOp.Substitute => PhoneCoverageState.Substitute,
                AlignmentOp.QueryExtra => withinSpan ? PhoneCoverageState.GapWithinSpan : PhoneCoverageState.OutsideSpan,
                _ => PhoneCoverageState.OutsideSpan,
            };
        }

        return queryPhonemes
            .Select((symbol, i) => new PhoneCoverageCellViewModel(symbol, states[i] ?? PhoneCoverageState.OutsideSpan))
            .ToList();
    }
}
