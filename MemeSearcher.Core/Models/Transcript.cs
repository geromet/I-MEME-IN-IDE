namespace MemeSearcher.Core.Models;

public class Transcript
{
    public Guid Id { get; set; }
    public Guid MediaId { get; set; }
    public required string Source { get; set; }
    public required string Language { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<Segment> Segments { get; set; } = [];
}
