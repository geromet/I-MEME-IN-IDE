using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Interfaces;

public record AlignedWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>Phone-level timing (Milestone 6) - optional because not every alignment provider produces it (WhisperX doesn't; MFA does).</summary>
public record AlignedPhone(string Symbol, double StartSeconds, double EndSeconds);

public record AlignmentResult(IReadOnlyList<AlignedWord> Words, IReadOnlyList<AlignedPhone>? Phones = null);

public interface IAlignmentProvider
{
    string ProviderName { get; }

    /// <summary>
    /// The alphabet this provider's phones are written in, or null when it produces no phone-level
    /// output at all (WhisperX is word-level only). Declared, not detected - see
    /// <see cref="IPhonemizer.Alphabet"/>.
    /// </summary>
    PhoneAlphabet? PhoneAlphabet { get; }

    Task<AlignmentResult> AlignAsync(string mediaPath, string transcriptText, CancellationToken cancellationToken = default);
}
