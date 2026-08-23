namespace MemeSearcher.Core.Models;

/// <summary>
/// One occurrence of a phoneme trigram in one media item's phoneme stream (#9) - the persistent
/// candidate-generation index. <see cref="StreamPosition"/> is the index into the same
/// <c>PhoneStreamEntry</c>/<c>PhoneToken</c> coordinate space <c>PhoneticSequenceMatcher</c> uses
/// for match Start/End, so a posting can seed a matcher window directly.
///
/// Derived data: every row here is reproducible from a media item's transcripts (Words/Phones)
/// via <c>PhoneNGramIndexer</c>, never authored directly, so it can always be thrown away and
/// rebuilt (addendum §39's layered-rebuildability rule).
/// </summary>
public class PhoneNGramPosting
{
    public Guid Id { get; set; }
    public Guid MediaId { get; set; }
    public required string NGram { get; set; }
    public int StreamPosition { get; set; }
}
