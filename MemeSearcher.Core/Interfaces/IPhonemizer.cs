namespace MemeSearcher.Core.Interfaces;

public record PhonemizedWord(string Text, string Ipa, IReadOnlyList<string> Phonemes);

public record PhonemizationResult(string Text, string Ipa, IReadOnlyList<PhonemizedWord> Words);

public interface IPhonemizer
{
    string ProviderName { get; }

    IReadOnlyCollection<string> SupportedLanguages { get; }

    Task<PhonemizationResult> PhonemizeAsync(string text, string language, CancellationToken cancellationToken = default);
}
