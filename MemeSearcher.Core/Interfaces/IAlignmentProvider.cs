using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Interfaces;

public record AlignedWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>Phone-level timing (Milestone 6) - optional because not every alignment provider produces it (WhisperX doesn't; MFA does).</summary>
public record AlignedPhone(string Symbol, double StartSeconds, double EndSeconds);

public record AlignmentResult(IReadOnlyList<AlignedWord> Words, IReadOnlyList<AlignedPhone>? Phones = null);

/// <summary>
/// One utterance to be aligned, in whole-file time coordinates (#33). MFA's alignment quality
/// depends on being given utterance boundaries rather than one monolithic block of text spanning
/// an entire recording - a single multi-thousand-word "utterance" over 15 minutes gives its beam
/// search one enormous path to find with no intermediate anchors, so any one unrecoverable stretch
/// (music, crosstalk, an out-of-dictionary run) fails the *whole* recording rather than one
/// utterance.
/// </summary>
public record AlignmentUtterance(double StartSeconds, double EndSeconds, string Text);

public interface IAlignmentProvider
{
    string ProviderName { get; }

    /// <summary>
    /// The alphabet this provider's phones are written in, or null when it produces no phone-level
    /// output at all (WhisperX is word-level only). Declared, not detected - see
    /// <see cref="IPhonemizer.Alphabet"/>.
    /// </summary>
    PhoneAlphabet? PhoneAlphabet { get; }

    /// <summary>
    /// Aligns each utterance's own text against its own span of the audio (#33) - not one blob
    /// covering the whole file. <paramref name="totalDurationSeconds"/> is the full media
    /// duration, needed to size the gap/silence regions around the given utterances; callers with
    /// no utterance-level timing at all should pass a single utterance spanning the whole file
    /// (reproducing the pre-#33 whole-transcript behaviour) rather than an empty list.
    /// </summary>
    Task<AlignmentResult> AlignAsync(
        string mediaPath,
        IReadOnlyList<AlignmentUtterance> utterances,
        double totalDurationSeconds,
        CancellationToken cancellationToken = default);
}
