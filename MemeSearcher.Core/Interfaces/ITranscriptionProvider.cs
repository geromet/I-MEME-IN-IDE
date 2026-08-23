namespace MemeSearcher.Core.Interfaces;

public record TranscribedSegment(double StartSeconds, double EndSeconds, string Text);

public record TranscriptionResult(string Language, IReadOnlyList<TranscribedSegment> Segments);

public interface ITranscriptionProvider
{
    string ProviderName { get; }

    Task<TranscriptionResult> TranscribeAsync(string mediaPath, string? language, CancellationToken cancellationToken = default);
}
