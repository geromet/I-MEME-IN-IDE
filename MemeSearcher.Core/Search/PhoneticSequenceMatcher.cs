using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Search;

/// <summary>
/// Correspondences records, for each Substitute step in the optimal alignment, the (0-based)
/// query and candidate indices involved - insert/delete steps don't produce a pair, since they
/// consume only one side. Composite search (Milestone 4) uses this to work out which part of the
/// query each source file's contribution covers; single-source search ignores it.
/// </summary>
public record PhoneticMatchSpan(int Start, int End, double Cost, IReadOnlyList<(int QueryIndex, int CandidateIndex)> Correspondences);

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
                var (start, correspondences) = Backtrace(move, n, runBestEnd);
                if (start > lastMatchEnd)
                {
                    matches.Add(new PhoneticMatchSpan(start, runBestEnd, runBestCost, correspondences));
                    lastMatchEnd = runBestEnd;
                }

                runBestCost = double.PositiveInfinity;
                runBestEnd = -1;
            }
        }

        return matches;
    }

    private static (int Start, IReadOnlyList<(int QueryIndex, int CandidateIndex)> Correspondences) Backtrace(Move[,] move, int i, int j)
    {
        var correspondences = new List<(int, int)>();

        while (i > 0)
        {
            switch (move[i, j])
            {
                case Move.Substitute:
                    correspondences.Add((i - 1, j - 1));
                    i--;
                    j--;
                    break;
                case Move.Delete:
                    j--;
                    break;
                case Move.Insert:
                    i--;
                    break;
            }
        }

        correspondences.Reverse();
        return (j, correspondences);
    }

    private static double MaxAcceptableCost(int queryLength, PhoneticSearchOptions options)
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
