namespace MemeSearcher.Core.Models;

/// <summary>
/// Milestone 17 (#20): a named, saved, curated set of sources - the durable counterpart to #13's
/// ad-hoc checkbox selection, which resets to nothing worth remembering the moment it changes.
/// Membership lives in <see cref="CatalogMedia"/>, keyed by <see cref="Media.Id"/> (already stable
/// across file moves, per #20's design note that identity must not be by path).
/// </summary>
public class Catalog
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
