namespace MemeSearcher.Core.Models;

public class Segment
{
    public Guid Id { get; set; }
    public Guid TranscriptId { get; set; }
    public int Sequence { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public required string Text { get; set; }
    public string? NormalizedText { get; set; }
    public string? Ipa { get; set; }
    public string? PhonemeSequence { get; set; }

    public List<Word> Words { get; set; } = [];
}
