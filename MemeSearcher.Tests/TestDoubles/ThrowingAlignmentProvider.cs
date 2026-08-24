using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Tests.TestDoubles;

/// <summary>Stands in for a failing MFA run (#33), so a realignment failure can be tested without a real mfa install.</summary>
public class ThrowingAlignmentProvider : IAlignmentProvider
{
    public string ProviderName => "throwing-aligner";

    public PhoneAlphabet? PhoneAlphabet => Core.Phonetics.PhoneAlphabet.Arpabet;

    public Task<AlignmentResult> AlignAsync(
        string mediaPath, IReadOnlyList<AlignmentUtterance> utterances, double totalDurationSeconds, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("mfa exited with code 1: NoAlignmentsError: There were no successful alignments for 1 utterances.");
}
