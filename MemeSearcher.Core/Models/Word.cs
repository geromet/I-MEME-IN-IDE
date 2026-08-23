namespace MemeSearcher.Core.Models;

public class Word
{
    public Guid Id { get; set; }
    public Guid SegmentId { get; set; }
    public int Sequence { get; set; }
    public required string Text { get; set; }
    public string? NormalizedText { get; set; }
    public string? Ipa { get; set; }
    public string? PhonemeSequence { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }

    public List<Phone> Phones { get; set; } = [];
}
