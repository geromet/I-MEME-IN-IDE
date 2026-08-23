using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Tests.TestDoubles;

/// <summary>Stands in for MFA (not installed on this machine) so MediaIngestionService.RealignAsync can be tested end-to-end.</summary>
public class FakeAlignmentProvider(
    AlignmentResult result,
    PhoneAlphabet? phoneAlphabet = MemeSearcher.Core.Phonetics.PhoneAlphabet.Arpabet)
    : IAlignmentProvider
{
    public string ProviderName => "fake-aligner";

    // Defaults to ARPABET because it stands in for MFA, which is what makes the two-alphabets
    // problem in #18 real.
    public PhoneAlphabet? PhoneAlphabet => phoneAlphabet;

    public string? LastMediaPath { get; private set; }
    public string? LastTranscriptText { get; private set; }

    public Task<AlignmentResult> AlignAsync(string mediaPath, string transcriptText, CancellationToken cancellationToken = default)
    {
        LastMediaPath = mediaPath;
        LastTranscriptText = transcriptText;
        return Task.FromResult(result);
    }
}
