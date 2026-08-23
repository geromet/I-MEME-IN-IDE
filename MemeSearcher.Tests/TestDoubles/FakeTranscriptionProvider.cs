using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Tests.TestDoubles;

/// <summary>
/// Stands in for whisperx (not installed on this machine) so the media-only ingestion path -
/// transcribe, then reuse the same phonemization/word-building pipeline as file-based transcripts
/// - can be tested end-to-end without the real binary.
/// </summary>
public class FakeTranscriptionProvider(IReadOnlyList<TranscribedSegment> segments) : ITranscriptionProvider
{
    public string ProviderName => "fake-transcriber";

    public string? LastMediaPath { get; private set; }
    public string? LastLanguage { get; private set; }

    public Task<TranscriptionResult> TranscribeAsync(string mediaPath, string? language, CancellationToken cancellationToken = default)
    {
        LastMediaPath = mediaPath;
        LastLanguage = language;
        return Task.FromResult(new TranscriptionResult(language ?? "unknown", segments));
    }
}
