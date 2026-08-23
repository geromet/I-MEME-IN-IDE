using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Interfaces;

public record PhonemizedWord(string Text, string Ipa, IReadOnlyList<string> Phonemes);

public record PhonemizationResult(string Text, string Ipa, IReadOnlyList<PhonemizedWord> Words);

public interface IPhonemizer
{
    string ProviderName { get; }

    IReadOnlyCollection<string> SupportedLanguages { get; }

    /// <summary>
    /// The alphabet this provider emits. Declared rather than detected: espeak-ng is known to
    /// write IPA, and guessing at something already known is how the untagged-data bug in #18
    /// happened. Detection exists to *validate* this claim, not to replace it.
    /// </summary>
    PhoneAlphabet Alphabet { get; }

    Task<PhonemizationResult> PhonemizeAsync(string text, string language, CancellationToken cancellationToken = default);
}
