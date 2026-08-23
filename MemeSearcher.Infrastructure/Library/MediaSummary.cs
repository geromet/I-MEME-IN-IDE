namespace MemeSearcher.Infrastructure.Library;

/// <summary>
/// Processing-status projection for the Library view (addendum §27: "every media item should
/// expose processing state"). Deliberately doesn't claim stages the current pipeline doesn't
/// have yet (no separate Video/Alignment/Index columns - addendum §27's mockup - since import is
/// currently one atomic step that always phonemizes); it reports what's actually knowable from
/// the data today rather than faking granularity.
/// </summary>
public record MediaSummary(
    Guid Id,
    string Title,
    string Path,
    bool HasPlayableMedia,
    TimeSpan Duration,
    string Language,
    DateTimeOffset CreatedAt,
    int SegmentCount,
    int WordCount,
    int PhonemizedWordCount);
