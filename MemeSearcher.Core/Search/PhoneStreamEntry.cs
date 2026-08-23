namespace MemeSearcher.Core.Search;

/// <summary>
/// One position in a media item's continuous phoneme stream (handoff §17), carrying provenance
/// back to the word it came from so a match span can be resolved to real timestamps and text.
/// Phone-level timing isn't populated yet (handoff §6: the Phone table can start sparse), so
/// every phoneme within a word currently shares that word's Start/EndSeconds.
/// </summary>
public sealed record PhoneStreamEntry(
    PhoneToken Token,
    Guid? SegmentId,
    Guid? WordId,
    string? WordText,
    double? StartSeconds,
    double? EndSeconds)
{
    public static PhoneStreamEntry Boundary() => new(PhoneToken.Boundary, null, null, null, null, null);

    public static PhoneStreamEntry Phoneme(
        string symbol, Guid segmentId, Guid wordId, string wordText, double startSeconds, double endSeconds) =>
        new(PhoneToken.Phoneme(symbol), segmentId, wordId, wordText, startSeconds, endSeconds);
}
