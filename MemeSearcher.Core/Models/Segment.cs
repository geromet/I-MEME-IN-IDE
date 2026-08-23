namespace MemeSearcher.Core.Models;

public class Segment
{
    public Guid Id { get; set; }
    public Guid TranscriptId { get; set; }
    public int Sequence { get; set; }
    /// <summary>Null when the transcript format carries no timing at all (#32), e.g. plain text.</summary>
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public required string Text { get; set; }
    public string? NormalizedText { get; set; }
    public string? Ipa { get; set; }
    public string? PhonemeSequence { get; set; }

    public List<Word> Words { get; set; } = [];
}
