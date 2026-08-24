using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Search;

/// <summary>
/// Correspondences records, for each Substitute step in the optimal alignment, the (0-based)
/// query and candidate indices involved - insert/delete steps don't produce a pair, since they
/// consume only one side. Composite search (Milestone 4) uses this to work out which part of the
/// query each source file's contribution covers; single-source search ignores it.
/// </summary>
public record PhoneticMatchSpan(int Start, int End, double Cost, IReadOnlyList<(int QueryIndex, int CandidateIndex)> Correspondences)
{
    /// <summary>
    /// Milestone 15 (#15): the full alignment path behind this match, not just its Substitute
    /// steps - <see cref="AlignmentStep"/> also records where a query phoneme was consumed with
    /// nothing in the candidate ("query has more than the match") and where a candidate phoneme
    /// was consumed with nothing in the query ("candidate has more than the query"), which is what
    /// "substitution/insertion positions visible against the query" actually requires. Kept as a
    /// separate field rather than folded into Correspondences, since both existing consumers
    /// (PhoneticSearchService, CompositeSearchService) depend on that field's Substitute-only shape.
    /// </summary>
    public IReadOnlyList<AlignmentStep> AlignmentSteps { get; init; } = [];
}

/// <summary>
/// One step of the edit-distance alignment between query and candidate (#15). Indices are 0-based
/// into the respective token lists; a step consumes only the side(s) its <see cref="AlignmentOp"/>
/// implies - <see cref="CandidateExtra"/> has no QueryIndex, <see cref="QueryExtra"/> has no
/// CandidateIndex.
/// </summary>
public record AlignmentStep(AlignmentOp Op, int? QueryIndex, int? CandidateIndex);

/// <summary>
/// Named from the UI's point of view, deliberately not from <c>PhoneticSequenceMatcher.Move</c>'s
/// DP terminology (Move.Delete/Move.Insert there mean the opposite of what a reader expects from
/// those words, since they describe editing the *query* into the *candidate*). CandidateExtra: the
/// candidate has a phoneme the query doesn't. QueryExtra: the query has a phoneme the candidate
/// doesn't.
/// </summary>
public enum AlignmentOp { Match, Substitute, CandidateExtra, QueryExtra }

/// <summary>
/// Approximate substring matching of a query phoneme sequence against a continuous candidate
/// phoneme stream (handoff §16: matches must be able to start/end mid-segment, not just align
/// whole segments). This is a free-start-position edit-distance DP (row 0 is all zero, so the
/// query can begin anywhere in the candidate), which is the standard technique for "does this
/// short pattern occur approximately somewhere in this long text" - as opposed to whole-sequence
/// Levenshtein, which would only tell you how different two full sequences are end-to-end.
///
/// This is an unoptimized full O(query * candidate) DP - correctness first per handoff §48/§18;
/// candidate generation (n-grams, skeletons) to cut the search space before running this is the
/// documented next step if profiling ever shows it's needed, not before.
/// </summary>
public static class PhoneticSequenceMatcher
{
    private enum Move : byte { Substitute, Delete, Insert }

    /// <summary>
    /// Re-applies FindLocalMinima's own "one best match per contiguous run" rule across match
    /// spans gathered from *separate* FindMatches calls over adjoining regions of the same
    /// candidate stream (#9's windowed candidate generation calls FindMatches once per window and
    /// concatenates the results). Each call's own suppression only sees its own window, so a run
    /// that straddles two windows would otherwise surface once per window instead of once overall -
    /// this makes windowed output structurally comparable to a single full-stream call again.
    /// Spans must already be in the same (full-stream) coordinate space.
    /// </summary>
    public static List<PhoneticMatchSpan> MergeAdjacentMatches(IEnumerable<PhoneticMatchSpan> matches)
    {
        var merged = new List<PhoneticMatchSpan>();

        foreach (var match in matches.OrderBy(m => m.Start))
        {
            if (merged.Count > 0 && match.Start <= merged[^1].End)
            {
                if (match.Cost < merged[^1].Cost)
                {
                    merged[^1] = match;
                }

                continue;
            }

            merged.Add(match);
        }

        return merged;
    }

    public static IReadOnlyList<PhoneticMatchSpan> FindMatches(
        IReadOnlyList<PhoneToken> query,
        IReadOnlyList<PhoneToken> candidate,
        PhoneticSearchOptions options)
    {
        var n = query.Count;
        var m = candidate.Count;

        if (n == 0 || m == 0)
        {
            return [];
        }

        var cost = new double[n + 1, m + 1];
        var move = new Move[n + 1, m + 1];

        for (var j = 0; j <= m; j++)
        {
            cost[0, j] = 0; // Free start: the query may begin at any candidate position.
        }

        for (var i = 1; i <= n; i++)
        {
            // A query phoneme with nothing to align to at the very start of the text - "deleted"
            // from the query's perspective.
            cost[i, 0] = cost[i - 1, 0] + DeletionCost(query[i - 1], options);
            move[i, 0] = Move.Insert;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var substitute = cost[i - 1, j - 1] + SubstitutionCost(query[i - 1], candidate[j - 1], options);
                // Candidate has a phoneme the query doesn't - "inserted" relative to the query.
                var delete = cost[i, j - 1] + InsertionCost(candidate[j - 1], options);
                // Query has a phoneme the candidate doesn't - "deleted" from the query to align.
                var insert = cost[i - 1, j] + DeletionCost(query[i - 1], options);

                var (best, chosen) = Min3(substitute, Move.Substitute, delete, Move.Delete, insert, Move.Insert);
                cost[i, j] = best;
                move[i, j] = chosen;
            }
        }

        return FindLocalMinima(query, candidate, cost, move, options);
    }

    private static List<PhoneticMatchSpan> FindLocalMinima(
        IReadOnlyList<PhoneToken> query,
        IReadOnlyList<PhoneToken> candidate,
        double[,] cost,
        Move[,] move,
        PhoneticSearchOptions options)
    {
        var n = query.Count;
        var m = candidate.Count;
        var threshold = MaxAcceptableCost(query.Count, options);

        var matches = new List<PhoneticMatchSpan>();
        var lastMatchEnd = -1;

        // Find the single best (lowest-cost) ending position within each contiguous
        // below-threshold run, rather than a pointwise 3-neighbor local-minimum check. A simple
        // "here <= prev && here <= next" test locks onto the first shallow dip it sees - which is
        // wrong whenever crossing a boundary token causes a small uptick partway through an
        // otherwise-improving run (e.g. a cross-file transition cost), stranding the match well
        // short of the true minimum a few positions further along the same run.
        var runBestCost = double.PositiveInfinity;
        var runBestEnd = -1;

        for (var j = 1; j <= m; j++)
        {
            var here = cost[n, j];
            var inRange = !double.IsPositiveInfinity(here) && here <= threshold;

            if (inRange && here < runBestCost)
            {
                runBestCost = here;
                runBestEnd = j;
            }

            var runEnded = !inRange || j == m;
            if (runEnded && runBestEnd != -1)
            {
                var (start, correspondences, alignmentSteps) = Backtrace(query, candidate, move, n, runBestEnd);
                if (start > lastMatchEnd)
                {
                    matches.Add(new PhoneticMatchSpan(start, runBestEnd, runBestCost, correspondences) { AlignmentSteps = alignmentSteps });
                    lastMatchEnd = runBestEnd;
                }

                runBestCost = double.PositiveInfinity;
                runBestEnd = -1;
            }
        }

        return matches;
    }

    private static (int Start, IReadOnlyList<(int QueryIndex, int CandidateIndex)> Correspondences, IReadOnlyList<AlignmentStep> AlignmentSteps) Backtrace(
        IReadOnlyList<PhoneToken> query, IReadOnlyList<PhoneToken> candidate, Move[,] move, int i, int j)
    {
        var correspondences = new List<(int, int)>();
        var steps = new List<AlignmentStep>();

        while (i > 0)
        {
            switch (move[i, j])
            {
                case Move.Substitute:
                    correspondences.Add((i - 1, j - 1));
                    // DP terminology: Move.Substitute covers both "same symbol" (cost 0) and a
                    // real substitution - distinguished here by symbol equality for the UI, which
                    // wants to tell an exact match from a substitution apart.
                    steps.Add(new AlignmentStep(
                        query[i - 1].Symbol == candidate[j - 1].Symbol ? AlignmentOp.Match : AlignmentOp.Substitute,
                        i - 1, j - 1));
                    i--;
                    j--;
                    break;
                case Move.Delete:
                    // Move.Delete: the candidate has a phoneme the query doesn't.
                    steps.Add(new AlignmentStep(AlignmentOp.CandidateExtra, null, j - 1));
                    j--;
                    break;
                case Move.Insert:
                    // Move.Insert: the query has a phoneme the candidate doesn't.
                    steps.Add(new AlignmentStep(AlignmentOp.QueryExtra, i - 1, null));
                    i--;
                    break;
            }
        }

        correspondences.Reverse();
        steps.Reverse();
        return (j, correspondences, steps);
    }

    /// <summary>
    /// The total edit cost a match may accept before <see cref="MinimumScore"/> would reject it.
    /// Public because #9's candidate generation needs the exact same number to bound how far a
    /// match could plausibly extend past the query's own length (via insertions/deletions) - a
    /// second, drifted copy of this formula would silently stop being "recall-safe" the moment one
    /// copy changed and the other didn't.
    /// </summary>
    public static double MaxAcceptableCost(int queryLength, PhoneticSearchOptions options)
    {
        var ceiling = queryLength * options.SubstitutionMaxCost;
        var normalizedFloor = 1 - options.MinimumScore;
        return double.IsPositiveInfinity(ceiling) ? double.PositiveInfinity : ceiling * normalizedFloor;
    }

    private static double SubstitutionCost(PhoneToken a, PhoneToken b, PhoneticSearchOptions options)
    {
        if (a.IsBoundary && b.IsBoundary)
        {
            return 0;
        }

        // Aligning a boundary directly against a real phoneme is never a good move; let the DP
        // route around it via insert/delete instead of taking this path.
        if (a.IsBoundary != b.IsBoundary)
        {
            return double.IsPositiveInfinity(options.SubstitutionMaxCost)
                ? double.PositiveInfinity
                : 2 * options.SubstitutionMaxCost;
        }

        return PhonemeFeatureTable.SubstitutionCost(a.Symbol, b.Symbol, options.SubstitutionMaxCost);
    }

    private static double InsertionCost(PhoneToken token, PhoneticSearchOptions options) =>
        BoundaryCost(token, options) ?? options.InsertionCost;

    private static double DeletionCost(PhoneToken token, PhoneticSearchOptions options) =>
        BoundaryCost(token, options) ?? options.DeletionCost;

    private static double? BoundaryCost(PhoneToken token, PhoneticSearchOptions options)
    {
        if (token.IsCrossFileBoundary)
        {
            return options.CrossFileTransitionCost;
        }

        return token.IsBoundary ? options.WordBoundaryCost : null;
    }

    private static (double Cost, Move Move) Min3(double c1, Move m1, double c2, Move m2, double c3, Move m3)
    {
        var best = c1;
        var chosen = m1;

        if (c2 < best)
        {
            best = c2;
            chosen = m2;
        }

        if (c3 < best)
        {
            best = c3;
            chosen = m3;
        }

        return (best, chosen);
    }
}
