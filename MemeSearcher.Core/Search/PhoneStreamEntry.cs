namespace MemeSearcher.Core.Search;

/// <summary>
/// One position in a media item's continuous phoneme stream (handoff §17), carrying provenance
/// back to the word - and, for phoneme entries, the media - it came from, so a match span can be
/// resolved to real timestamps, text, and (Milestone 4) which source file each part came from.
/// Timing is nullable throughout: a phone falls back to its word's span when it has none of its
/// own, and a word may have no timing at all when its transcript never had any (#32).
/// </summary>
public sealed record PhoneStreamEntry(
    PhoneToken Token,
    Guid? MediaId,
    Guid? SegmentId,
    Guid? WordId,
    string? WordText,
    double? StartSeconds,
    double? EndSeconds,
    // Milestone 15 (#15): whether this phoneme's timing came from a real per-phone Phone row
    // (MFA alignment ran) versus inheriting its whole word's single span - the distinction the
    // inspector needs to visibly label a phone block "aligned" vs "estimated" (handoff §49).
    // Derived once here rather than re-derived from timing shape at display time, since a
    // single-phoneme word's aligned and estimated spans are otherwise indistinguishable.
    bool IsPhoneLevelAligned = false)
{
    public static PhoneStreamEntry Boundary() => new(PhoneToken.Boundary, null, null, null, null, null, null);

    public static PhoneStreamEntry CrossFileBoundary() => new(PhoneToken.CrossFileBoundary, null, null, null, null, null, null);

    public static PhoneStreamEntry Phoneme(
        string symbol, Guid mediaId, Guid segmentId, Guid wordId, string wordText,
        double? startSeconds, double? endSeconds, bool isPhoneLevelAligned) =>
        new(PhoneToken.Phoneme(symbol), mediaId, segmentId, wordId, wordText, startSeconds, endSeconds, isPhoneLevelAligned);
}
