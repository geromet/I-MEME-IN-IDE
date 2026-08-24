using MemeSearcher.Core.Search;

namespace MemeSearcher.Core.Models;

/// <summary>
/// Milestone 18 (#21): a named, saved query defined as a hand-authored phone sequence rather than
/// text - bypasses the phonemizer entirely (see TemplateSearchService), so it can express sounds
/// that have no spelling: a specific laugh, a stutter, a catchphrase said wrong in the way that
/// made it a meme (handoff §49). Search config is bundled per-template (handoff §31) rather than
/// left as ambient search-bar state, since a template that finds its target at loose settings and
/// nothing at strict ones should carry the settings that work.
/// </summary>
public class Template
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    public SearchMode Mode { get; set; } = SearchMode.SimilarPhonetic;

    /// <summary>
    /// JSON-serialized <see cref="PhoneticSearchOptions"/> override, or null to use
    /// <see cref="PhoneticSearchOptions.ForMode"/>'s defaults for <see cref="Mode"/>. Kept as a
    /// blob rather than individual columns since the option set is a single unit that only ever
    /// round-trips as a whole (never queried field-by-field), and PhoneticSearchOptions already
    /// exists as a plain record built for exactly this shape.
    /// </summary>
    public string? SearchOptionsJson { get; set; }

    /// <summary>
    /// Milestone 17 tie-in: the catalog this template searches by default, or null to search all
    /// indexed media. Nulled (not cascade-deleted) if the catalog is removed - the template must
    /// survive losing its target, the same way a catalog surviving a removed source does (#20).
    /// </summary>
    public Guid? TargetCatalogId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
