namespace MemeSearcher.Core.Interfaces;

/// <summary>Real measured timing for one word, when the provider has it (Milestone 5) - as opposed to the interpolated placeholder used when it doesn't.</summary>
public record TranscribedWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>Words is null when the provider only produces segment-level timing (e.g. no alignment step ran).</summary>
public record TranscribedSegment(double StartSeconds, double EndSeconds, string Text, IReadOnlyList<TranscribedWord>? Words = null);

/// <summary>
/// What actually produced a transcript, recorded so already-ingested media stays interpretable
/// after the settings that produced it change (#24). A corpus half-transcribed with `tiny` and
/// half with `large-v3` is not internally comparable, and without this nothing distinguishes the
/// two. Null when the transcript did not come from a transcription run (an imported SRT).
/// </summary>
/// <param name="Model">Model name, e.g. "small".</param>
/// <param name="Device">Resolved device actually used - never "auto".</param>
/// <param name="ComputeType">Compute type as passed to the tool.</param>
public record TranscriptionProvenance(string Model, string Device, string ComputeType);

public record TranscriptionResult(
    string Language,
    IReadOnlyList<TranscribedSegment> Segments,
    TranscriptionProvenance? Provenance = null);

public interface ITranscriptionProvider
{
    string ProviderName { get; }

    Task<TranscriptionResult> TranscribeAsync(string mediaPath, string? language, CancellationToken cancellationToken = default);
}
