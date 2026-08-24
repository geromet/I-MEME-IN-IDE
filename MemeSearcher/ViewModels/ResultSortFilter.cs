using System.Collections.Generic;
using System.Linq;

namespace MemeSearcher.ViewModels;

/// <summary>
/// #25 exit criterion 2's actual ordering/filtering rule, pulled out of SearchViewModel so it's
/// testable without a database, a phonemizer, or espeak - just a list of already-built rows.
/// </summary>
public static class ResultSortFilter
{
    public static IEnumerable<SearchResultRowViewModel> Apply(
        IEnumerable<SearchResultRowViewModel> results, ResultSortMode sortMode, double minimumCoverage)
    {
        var filtered = results.Where(r => r.CoverageFraction >= minimumCoverage);

        // Score order is exactly what the server already returned - re-sorting by the same key it
        // used would just be redundant client-side work, so Score mode passes the filtered
        // sequence through unchanged rather than re-sorting it.
        return sortMode == ResultSortMode.Coverage
            ? filtered.OrderByDescending(r => r.CoverageFraction).ThenByDescending(r => r.Score)
            : filtered;
    }
}
