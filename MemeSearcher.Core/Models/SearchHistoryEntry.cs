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
    public int ResultCount { get; set; }
    public DateTimeOffset SearchedAt { get; set; }
}
