namespace MemeSearcher.Core.Search;

/// <summary>
/// One position in a media item's continuous phoneme stream (handoff §17), carrying provenance
/// back to the word - and, for phoneme entries, the media - it came from, so a match span can be
/// resolved to real timestamps, text, and (Milestone 4) which source file each part came from.
/// Phone-level timing isn't populated yet (handoff §6: the Phone table can start sparse), so
/// every phoneme within a word currently shares that word's Start/EndSeconds.
/// </summary>
public sealed record PhoneStreamEntry(
    PhoneToken Token,
    Guid? MediaId,
    Guid? SegmentId,
    Guid? WordId,
    string? WordText,
    double? StartSeconds,
    double? EndSeconds)
{
    public static PhoneStreamEntry Boundary() => new(PhoneToken.Boundary, null, null, null, null, null, null);

    public static PhoneStreamEntry CrossFileBoundary() => new(PhoneToken.CrossFileBoundary, null, null, null, null, null, null);

    public static PhoneStreamEntry Phoneme(
        string symbol, Guid mediaId, Guid segmentId, Guid wordId, string wordText, double startSeconds, double endSeconds) =>
        new(PhoneToken.Phoneme(symbol), mediaId, segmentId, wordId, wordText, startSeconds, endSeconds);
}
