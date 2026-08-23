using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Tests.TestDoubles;

/// <summary>Stands in for MFA (not installed on this machine) so MediaIngestionService.RealignAsync can be tested end-to-end.</summary>
public class FakeAlignmentProvider(AlignmentResult result) : IAlignmentProvider
{
    public string ProviderName => "fake-aligner";

    public string? LastMediaPath { get; private set; }
    public string? LastTranscriptText { get; private set; }

    public Task<AlignmentResult> AlignAsync(string mediaPath, string transcriptText, CancellationToken cancellationToken = default)
    {
        LastMediaPath = mediaPath;
        LastTranscriptText = transcriptText;
        return Task.FromResult(result);
    }
}
