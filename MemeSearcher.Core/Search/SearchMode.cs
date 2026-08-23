namespace MemeSearcher.Core.Search;

/// <summary>handoff §19. Default is Similar.</summary>
public enum SearchMode
{
    ExactPhonetic,
    FuzzyPhonetic,
    SimilarPhonetic,
    LoosePhonetic,
}
