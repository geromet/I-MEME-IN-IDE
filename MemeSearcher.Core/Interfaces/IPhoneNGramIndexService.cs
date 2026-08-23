namespace MemeSearcher.Core.Interfaces;

/// <summary>Outcome of a from-scratch reindex (#9) - how much of the corpus was rebuilt.</summary>
public record ReindexSummary(int MediaCount, int PostingCount);

/// <summary>
/// Maintains the persistent phoneme n-gram index (#9) that <c>PhoneticSearchService</c> uses for
/// candidate generation. The index is derived data - always reproducible from a media item's
/// stored transcripts/phones, never authored directly - so it must be independently rebuildable
/// without retranscribing (addendum §39's layered-rebuildability rule, handoff §30).
/// </summary>
public interface IPhoneNGramIndexService
{
    /// <summary>
    /// Rebuilds one media item's postings from its current transcripts, replacing whatever was
    /// there before. Called after ingestion and after any operation - such as realignment - that
    /// changes phone-level content, since a stale posting pointing at phones that no longer exist
    /// is worse than no index at all.
    /// </summary>
    Task IndexMediaAsync(Guid mediaId, CancellationToken cancellationToken = default);

    /// <summary>Rebuilds every media item's postings from scratch - the callable "reindex/repair" operation #9 requires.</summary>
    Task<ReindexSummary> ReindexAllAsync(CancellationToken cancellationToken = default);
}
