namespace MemeSearcher.Core.Phonetics;

/// <summary>
/// A phone in canonical form: an IPA symbol with stress carried separately rather than glued on.
/// </summary>
/// <param name="Symbol">Canonical IPA symbol, stress-free.</param>
/// <param name="Stress">0 unstressed, 1 primary, 2 secondary; null when the source did not say.</param>
public record CanonicalPhone(string Symbol, int? Stress);

/// <summary>
/// Converts stored phone symbols into the canonical alphabet (IPA) for matching (#18).
///
/// Nothing derived by this class is persisted. Storage keeps what each provider actually wrote,
/// tagged with its alphabet, and canonical forms are produced on read - so a mistake in the table
/// below is fixed by editing this file, with no need to re-run alignment against source media that
/// may no longer exist. When the persistent index lands (#9) it materialises these values; that is
/// what makes a conversion fix a reindex rather than a realignment.
///
/// **The ARPABET conversion is not mechanical, and naive digit-stripping is wrong twice over.**
/// The stress digit is not decoration: AH0 and AH1 are different vowels in IPA (ə vs ʌ), not one
/// vowel plus a mark. So the digit has to be read *before* it is removed, and it then has to be
/// kept somewhere - hence <see cref="CanonicalPhone.Stress"/> rather than folding it into the
/// symbol or discarding it. ER0/ER1 are the same story (ɚ vs ɜː).
///
/// Targets are espeak-ng's actual en-us realisations, verified against the binary, because those
/// are the symbols <see cref="PhonemeFeatureTable"/> is built from. Converting to idealised IPA
/// instead would produce symbols the feature table does not know, which it charges as unknown -
/// the same silent quality loss this issue exists to remove.
/// </summary>
public static class PhoneAlphabetConverter
{
    private const char PrimaryStress = 'ˈ';
    private const char SecondaryStress = 'ˌ';

    private static readonly Dictionary<string, string> ArpabetConsonants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B"] = "b", ["CH"] = "tʃ", ["D"] = "d", ["DH"] = "ð", ["F"] = "f", ["G"] = "ɡ",
        ["HH"] = "h", ["JH"] = "dʒ", ["K"] = "k", ["L"] = "l", ["M"] = "m", ["N"] = "n",
        ["NG"] = "ŋ", ["P"] = "p", ["R"] = "ɹ", ["S"] = "s", ["SH"] = "ʃ", ["T"] = "t",
        ["TH"] = "θ", ["V"] = "v", ["W"] = "w", ["Y"] = "j", ["Z"] = "z", ["ZH"] = "ʒ",
    };

    private static readonly Dictionary<string, string> ArpabetVowels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AA"] = "ɑː", ["AE"] = "æ", ["AO"] = "ɔː", ["AW"] = "aʊ", ["AY"] = "aɪ",
        ["EH"] = "ɛ", ["EY"] = "eɪ", ["IH"] = "ɪ", ["IY"] = "iː", ["OW"] = "oʊ",
        ["OY"] = "ɔɪ", ["UH"] = "ʊ", ["UW"] = "uː",
    };

    /// <summary>
    /// The two vowels whose IPA realisation depends on stress. Everything else maps the same way
    /// regardless, which is exactly why these two are easy to get wrong.
    /// </summary>
    private static readonly Dictionary<string, (string Unstressed, string Stressed)> StressDependentVowels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // AH0 is the reduced vowel (schwa); AH1/AH2 is the STRUT vowel. Different phonemes.
            ["AH"] = ("ə", "ʌ"),
            // ER0 is r-coloured schwa (butter); ER1/ER2 is the NURSE vowel (bird).
            ["ER"] = ("ɚ", "ɜː"),
        };

    public static IReadOnlyList<CanonicalPhone> ToCanonical(IEnumerable<string> symbols, PhoneAlphabet alphabet) =>
        symbols.Select(s => ToCanonical(s, alphabet)).Where(p => p.Symbol.Length > 0).ToList();

    public static CanonicalPhone ToCanonical(string symbol, PhoneAlphabet alphabet) => alphabet switch
    {
        PhoneAlphabet.Ipa => FromIpa(symbol),
        PhoneAlphabet.Arpabet => FromArpabet(symbol),
        _ => throw new ArgumentOutOfRangeException(nameof(alphabet), alphabet, null),
    };

    /// <summary>
    /// IPA is already canonical; the only work is lifting stress marks out of the symbol so an
    /// IPA-sourced phone compares equal to the same ARPABET-sourced phone. Stored espeak output
    /// has already had these stripped by IpaTokenizer, so this mostly matters for user-pasted
    /// input, which arrives with the marks intact.
    /// </summary>
    private static CanonicalPhone FromIpa(string symbol)
    {
        int? stress = symbol.Contains(PrimaryStress) ? 1
            : symbol.Contains(SecondaryStress) ? 2
            : null;

        return new CanonicalPhone(symbol.Trim(PrimaryStress, SecondaryStress), stress);
    }

    private static CanonicalPhone FromArpabet(string symbol)
    {
        if (!ArpabetInventory.TrySplit(symbol, out var baseSymbol, out var stress))
        {
            // Not ARPABET-shaped at all. Pass it through rather than dropping it: an unknown
            // symbol scores as unknown in the feature table, which is honest, whereas silently
            // deleting a phone would shorten the sequence and corrupt every alignment around it.
            return new CanonicalPhone(symbol, null);
        }

        if (StressDependentVowels.TryGetValue(baseSymbol, out var pair))
        {
            // Read the digit before discarding it - this is the case naive stripping gets wrong.
            return new CanonicalPhone(stress is 0 or null ? pair.Unstressed : pair.Stressed, stress);
        }

        if (ArpabetVowels.TryGetValue(baseSymbol, out var vowel))
        {
            return new CanonicalPhone(vowel, stress);
        }

        return ArpabetConsonants.TryGetValue(baseSymbol, out var consonant)
            ? new CanonicalPhone(consonant, stress)
            : new CanonicalPhone(symbol, stress);
    }
}
