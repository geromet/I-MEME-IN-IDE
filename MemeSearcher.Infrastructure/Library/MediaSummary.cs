namespace MemeSearcher.Infrastructure.Library;

/// <summary>
/// Factual processing-state projection for the Library view (#34 / addendum §27-28).
/// Counts stay separate from presentation so the UI can distinguish none/partial/full without
/// inventing a threshold. Index state deliberately means "has persisted n-gram postings" - the
/// concrete per-media fact available in the current schema.
/// </summary>
public record MediaSummary(
    Guid Id,
    string Title,
    string Path,
    bool HasPlayableMedia,
    TimeSpan Duration,
    string Language,
    DateTimeOffset CreatedAt,
    bool HasTranscript,
    int SegmentCount,
    int WordCount,
    int PhonemizedWordCount,
    int AlignedWordCount,
    bool HasIndexPostings,
    bool IsSelectedForSearch);
