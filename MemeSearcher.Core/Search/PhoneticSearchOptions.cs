namespace MemeSearcher.Core.Search;

/// <summary>
/// Configurable knobs for the fuzzy matcher (handoff §31) - deliberately not hard-coded into
/// PhoneticSequenceMatcher itself.
/// </summary>
public record PhoneticSearchOptions
{
    public double InsertionCost { get; init; } = 1.0;
    public double DeletionCost { get; init; } = 1.0;
    public double SubstitutionMaxCost { get; init; } = 1.0;

    /// <summary>Cost of crossing a word boundary - low by default so "ice cream" / "I scream" remain comparable.</summary>
    public double WordBoundaryCost { get; init; } = 0.1;

    public double MinimumScore { get; init; } = 0.5;
    public int MaxResults { get; init; } = 25;

    // Milestone 4 - composite (multi-file) search only.

    /// <summary>
    /// Cost of crossing from one source file to another - deliberately higher than
    /// WordBoundaryCost (addendum §19) so results don't casually stitch together unrelated clips
    /// when a single-file match would do.
    /// </summary>
    public double CrossFileTransitionCost { get; init; } = 0.5;

    /// <summary>Composite results using more distinct source files than this are rejected (addendum §20).</summary>
    public int MaxSourceFiles { get; init; } = 4;

    /// <summary>A source contributing fewer phonemes than this to a composite result is treated as noise, not a real contribution (addendum §20).</summary>
    public int MinPhonemesPerSource { get; init; } = 2;

    public static PhoneticSearchOptions ForMode(SearchMode mode) => mode switch
    {
        SearchMode.ExactPhonetic => new PhoneticSearchOptions
        {
            InsertionCost = double.PositiveInfinity,
            DeletionCost = double.PositiveInfinity,
            SubstitutionMaxCost = double.PositiveInfinity,
            WordBoundaryCost = 0,
            MinimumScore = 1.0,
        },
        SearchMode.FuzzyPhonetic => new PhoneticSearchOptions
        {
            InsertionCost = 1.0,
            DeletionCost = 1.0,
            SubstitutionMaxCost = 1.0,
            WordBoundaryCost = 0.1,
            MinimumScore = 0.5,
        },
        SearchMode.SimilarPhonetic => new PhoneticSearchOptions(),
        SearchMode.LoosePhonetic => new PhoneticSearchOptions
        {
            InsertionCost = 0.5,
            DeletionCost = 0.5,
            SubstitutionMaxCost = 1.0,
            WordBoundaryCost = 0.05,
            MinimumScore = 0.35,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
