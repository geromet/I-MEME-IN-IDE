using System.Collections.Generic;
using System.Linq;

namespace MemeSearcher.ViewModels;

/// <summary>Pulled out of SearchViewModel for the same reason as ResultSortFilter - testable against a plain row list, no database or espeak involved.</summary>
public static class ResultGrouping
{
    /// <summary>
    /// Groups by the exact [QueryStart, QueryEnd) span each row covers. Members within a group keep
    /// the order they arrived in (i.e. whatever ResultSortFilter already produced - grouping doesn't
    /// re-rank). Groups are ordered by QueryStart, then QueryEnd, so scanning top to bottom reads
    /// left to right across the query - mirroring the coverage strip above them, and matching the
    /// issue's own framing ("these clips each cover part of your query", read in query order).
    /// </summary>
    public static IReadOnlyList<ResultGroupViewModel> GroupByCoveredSpan(IEnumerable<SearchResultRowViewModel> rows)
    {
        return rows
            .GroupBy(r => (r.QueryStart, r.QueryEnd))
            .OrderBy(g => g.Key.QueryStart)
            .ThenBy(g => g.Key.QueryEnd)
            .Select(g => new ResultGroupViewModel(BuildLabel(g.Key.QueryStart, g.Key.QueryEnd, g.First()), g.ToList()))
            .ToList();
    }

    private static string BuildLabel(int queryStart, int queryEnd, SearchResultRowViewModel sample)
    {
        if (queryEnd <= queryStart || sample.QueryPhonemes.Count == 0)
        {
            return "No query coverage";
        }

        var covered = string.Join(' ', sample.QueryPhonemes.Skip(queryStart).Take(queryEnd - queryStart));
        return $"Covers \"{covered}\"";
    }
}
