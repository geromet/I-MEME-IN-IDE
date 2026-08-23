using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Search;

public record PhoneticMatchSpan(int Start, int End, double Cost);

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

        for (var j = 1; j <= m; j++)
        {
            var here = cost[n, j];
            if (double.IsPositiveInfinity(here) || here > threshold)
            {
                continue;
            }

            var prev = cost[n, j - 1];
            var next = j < m ? cost[n, j + 1] : double.PositiveInfinity;

            var isLocalMinimum = here <= prev && here <= next;
            if (!isLocalMinimum)
            {
                continue;
            }

            var start = Backtrace(move, n, j);
            if (start <= lastMatchEnd)
            {
                continue; // Overlaps the previous match - skip rather than emit near-duplicates.
            }

            matches.Add(new PhoneticMatchSpan(start, j, here));
            lastMatchEnd = j;
        }

        return matches;
    }

    private static int Backtrace(Move[,] move, int i, int j)
    {
        while (i > 0)
        {
            switch (move[i, j])
            {
                case Move.Substitute:
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

        return j;
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
        token.IsBoundary ? options.WordBoundaryCost : options.InsertionCost;

    private static double DeletionCost(PhoneToken token, PhoneticSearchOptions options) =>
        token.IsBoundary ? options.WordBoundaryCost : options.DeletionCost;

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
