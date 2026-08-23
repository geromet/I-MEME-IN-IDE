using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Tests.TestDoubles;

/// <summary>
/// For tests that always supply a transcript file and should never hit the
/// transcribe-the-media-directly path - throws if that assumption is ever violated, rather than
/// silently succeeding with a fake transcript that would mask a real wiring bug.
/// </summary>
public class UnusedTranscriptionProvider : ITranscriptionProvider
{
    public string ProviderName => "unused";

    public Task<TranscriptionResult> TranscribeAsync(string mediaPath, string? language, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("This test should not need transcription - a transcript file should have been provided.");
}
