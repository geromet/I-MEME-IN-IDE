namespace MemeSearcher.Core.Interfaces;

/// <summary>Real measured timing for one word, when the provider has it (Milestone 5) - as opposed to the interpolated placeholder used when it doesn't.</summary>
public record TranscribedWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>Words is null when the provider only produces segment-level timing (e.g. no alignment step ran).</summary>
public record TranscribedSegment(double StartSeconds, double EndSeconds, string Text, IReadOnlyList<TranscribedWord>? Words = null);

public record TranscriptionResult(string Language, IReadOnlyList<TranscribedSegment> Segments);

public interface ITranscriptionProvider
{
    string ProviderName { get; }

    Task<TranscriptionResult> TranscribeAsync(string mediaPath, string? language, CancellationToken cancellationToken = default);
}
