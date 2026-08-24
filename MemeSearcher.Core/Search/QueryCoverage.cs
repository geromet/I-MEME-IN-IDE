namespace MemeSearcher.Core.Search;

/// <summary>
/// Maps a match's per-step query indices - raw <see cref="PhoneToken"/> index space, which includes
/// the boundary tokens PhoneStreamBuilder.BuildQueryTokens inserts between every pair of words - onto
/// positions in the boundary-filtered query-phoneme list every result actually surfaces
/// (SearchResult.QueryPhonemes, CompositeSearchResult.QueryPhonemes). Composite search's
/// CompositeMatchComponent.QueryStart/QueryEnd previously indexed raw queryTokens directly, which is
/// only correct for a single-word query - any query of two or more words silently returned a span
/// shifted by however many boundary tokens preceded it (#25). Both single-source and composite
/// search now go through this so there is one coordinate space, not two.
/// </summary>
public static class QueryCoverage
{
    /// <summary>index[i] is the boundary-filtered position of raw queryTokens[i], or -1 if queryTokens[i] is itself a boundary.</summary>
    public static int[] BuildIndexMap(IReadOnlyList<PhoneToken> queryTokens)
    {
        var map = new int[queryTokens.Count];
        var next = 0;

        for (var i = 0; i < queryTokens.Count; i++)
        {
            map[i] = queryTokens[i].IsBoundary ? -1 : next++;
        }

        return map;
    }

    /// <summary>
    /// The [Start, End) envelope, in filtered-index space, of every position actually reached by a
    /// real correspondence - i.e. the query span this match covers, as opposed to phonemes the query
    /// asked for that this candidate simply doesn't have (AlignmentOp.QueryExtra). Empty when given
    /// no indices at all (a match with no real correspondence to anything, which MinimumScore should
    /// already have filtered out upstream - this is a defensive default, not an expected case).
    /// </summary>
    public static (int Start, int End) ComputeSpan(IEnumerable<int> coveredIndices)
    {
        var min = int.MaxValue;
        var max = int.MinValue;

        foreach (var index in coveredIndices)
        {
            if (index < min) min = index;
            if (index > max) max = index;
        }

        return max >= min ? (min, max + 1) : (0, 0);
    }
}
