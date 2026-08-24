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

    /// <summary>
    /// Null for a template-driven entry (Milestone 18/#21) - a hand-authored phone sequence has no
    /// text query to show, and stuffing the template's name in here would be exactly the
    /// "reconstructed string" #21 warns against: it would read as a query the search actually ran,
    /// when it didn't. <see cref="TemplateId"/>/<see cref="TemplateName"/> carry that case instead.
    /// </summary>
    public string? QueryText { get; set; }

    /// <summary>Null for a template-driven entry - a phone-token search bypasses the phonemizer entirely (#21), so no language ever applied to it.</summary>
    public string? Language { get; set; }

    public bool IsComposite { get; set; }
    public required string ScopeDescription { get; set; }

    /// <summary>
    /// Which template was run, or null for an ordinary text search. SetNull on the template's own
    /// deletion (see MemeSearcherDbContext) rather than cascading the history row away - a deleted
    /// template's past runs are still a real fact about what was searched.
    /// </summary>
    public Guid? TemplateId { get; set; }

    /// <summary>
    /// Denormalized at record time, not looked up live - so a later rename or deletion of the
    /// template doesn't rewrite what this history entry says it ran. Null exactly when
    /// <see cref="TemplateId"/> is null.
    /// </summary>
    public string? TemplateName { get; set; }

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
