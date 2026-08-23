namespace MemeSearcher.Core.Interfaces;

public record AlignedWord(string Text, double StartSeconds, double EndSeconds);

public record AlignmentResult(IReadOnlyList<AlignedWord> Words);

public interface IAlignmentProvider
{
    string ProviderName { get; }

    Task<AlignmentResult> AlignAsync(string mediaPath, string transcriptText, CancellationToken cancellationToken = default);
}
