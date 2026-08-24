using System;
using System.Linq;
using MemeSearcher.Core.Search;

namespace MemeSearcher.Core.Models;

/// <summary>
/// Addendum §35: a convenience record of recent searches, not a data source in its own right -
/// re-running one still goes through the real search services, never reads cached results out
/// of this table.
/// </summary>
public class SearchHistoryEntry
{
    public Guid Id { get; set; }
    public required string QueryText { get; set; }
    public required string Language { get; set; }
    public bool IsComposite { get; set; }
    public required string ScopeDescription { get; set; }

    /// <summary>
    /// Milestone 13: the actual scope, not just its display text - required for
    /// "History entries round-trip their scope" (re-running an entry must reproduce the same
    /// scope it was run with, not whatever the library panel's checkboxes currently show). Null
    /// means "all indexed media" and is resolved against the corpus at rerun time, same as it was
    /// at search time; a comma-joined list means an explicit, frozen set of media - including ones
    /// since removed from the library, which simply resolve to no results rather than an error.
    /// </summary>
    public string? SelectedMediaIdsCsv { get; set; }

    public int ResultCount { get; set; }
    public DateTimeOffset SearchedAt { get; set; }

    /// <summary>The one place that parses <see cref="SelectedMediaIdsCsv"/> back into a scope, so RerunAsync-style callers can't drift from RecordAsync's format.</summary>
    public SearchScope ToSearchScope() =>
        string.IsNullOrEmpty(SelectedMediaIdsCsv)
            ? new SearchScope.AllIndexedMedia()
            : new SearchScope.SelectedMedia(SelectedMediaIdsCsv.Split(',').Select(Guid.Parse).ToList());
}
