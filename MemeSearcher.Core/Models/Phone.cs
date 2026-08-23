namespace MemeSearcher.Core.Models;

public class Phone
{
    public Guid Id { get; set; }
    public Guid WordId { get; set; }
    public int Sequence { get; set; }
    public required string Symbol { get; set; }
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
}
