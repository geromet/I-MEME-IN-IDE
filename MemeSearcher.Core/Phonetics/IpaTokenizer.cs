namespace MemeSearcher.Core.Phonetics;

/// <summary>
/// Splits an espeak-ng `--sep=_`-delimited word group (e.g. "m_ˈæ_s_ɪ_v") into individual
/// phoneme symbols. Built against espeak-ng's actual output, not idealized IPA: espeak already
/// keeps multi-codepoint phonemes (affricates like "dʒ", long vowels like "ɑː") as single
/// underscore-delimited tokens, so tokenization itself is just a split. The one thing we still
/// have to strip is stress (ˈ primary, ˌ secondary) - a prosodic, word-level property glued onto
/// the following phoneme's symbol - which would otherwise make the same vowel compare as a
/// different phoneme depending on where stress falls.
/// </summary>
public static class IpaTokenizer
{
    private const char PrimaryStress = 'ˈ';
    private const char SecondaryStress = 'ˌ';

    public static IReadOnlyList<string> TokenizeWordGroup(string sepDelimitedGroup)
    {
        if (string.IsNullOrEmpty(sepDelimitedGroup))
        {
            return [];
        }

        return sepDelimitedGroup
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(StripStress)
            .Where(phone => phone.Length > 0)
            .ToList();
    }

    private static string StripStress(string phone) => phone.Trim(PrimaryStress, SecondaryStress);
}
