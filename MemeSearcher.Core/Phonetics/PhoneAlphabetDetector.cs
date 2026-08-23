namespace MemeSearcher.Core.Phonetics;

/// <summary>
/// The outcome of alphabet detection. <see cref="Alphabet"/> is null when the input is genuinely
/// undecidable - the caller must then ask, not guess.
/// </summary>
/// <param name="Alphabet">The detected alphabet, or null when undecidable.</param>
/// <param name="Confidence">0..1. Compare against <see cref="PhoneAlphabetDetector.ConfidenceThreshold"/>.</param>
/// <param name="Explanation">What the decision was based on, for error messages and prompts.</param>
public record AlphabetDetection(PhoneAlphabet? Alphabet, double Confidence, string Explanation)
{
    public bool IsConfident =>
        Alphabet is not null && Confidence >= PhoneAlphabetDetector.ConfidenceThreshold;
}

/// <summary>
/// Works out which alphabet a sequence of phone symbols is written in (#18).
///
/// Two jobs, and they are not the same job. On the corpus side the alphabet is *known* - espeak
/// emits IPA, MFA emits ARPABET - so providers declare it and this only validates the
/// declaration. On the user-input side (a template pasted from CMUdict, which is ARPABET, or from
/// Wiktionary, which is IPA) nothing declares anything and this has to decide.
///
/// It must refuse to guess. A wrong detection is exactly as silent as the bug this whole issue is
/// about: the query simply never matches, with no error anywhere. So genuinely ambiguous input
/// returns a null alphabet and the caller prompts.
///
/// What is actually distinguishable:
/// - Any non-ASCII character, or an IPA stress/length mark, is unambiguously IPA. ASCII cannot
///   express most IPA vowels or the affricates.
/// - A trailing stress digit on an ARPABET vowel is unambiguously ARPABET. IPA never writes digits.
/// - All-uppercase tokens drawn from the ARPABET inventory are confidently ARPABET.
/// - Everything else is ambiguous, and the ambiguity is real: p b t d k g m n f v s z h l r w j are
///   all simultaneously valid IPA symbols and valid ARPABET-modulo-case. Case is the only
///   discriminator left and it is weak, because people write ARPABET in lowercase constantly.
/// </summary>
public static class PhoneAlphabetDetector
{
    public const double ConfidenceThreshold = 0.8;

    private const char PrimaryStress = 'ˈ';
    private const char SecondaryStress = 'ˌ';
    private const char LengthMark = 'ː';

    public static AlphabetDetection Detect(string spaceSeparatedSymbols) =>
        Detect(spaceSeparatedSymbols.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static AlphabetDetection Detect(IEnumerable<string> symbols)
    {
        var tokens = symbols.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        if (tokens.Count == 0)
        {
            return new AlphabetDetection(null, 0, "No symbols to inspect.");
        }

        if (tokens.Any(HasIpaOnlyCharacter))
        {
            return new AlphabetDetection(
                PhoneAlphabet.Ipa, 1.0,
                "Contains characters that only IPA uses (non-ASCII, or a stress/length mark).");
        }

        // Every token is now pure ASCII, which is where the two alphabets overlap.
        var split = tokens.Select(t => (Token: t, Ok: ArpabetInventory.TrySplit(t, out var b, out var s), Base: b, Stress: s)).ToList();

        if (split.Any(t => t.Ok && t.Stress is not null && ArpabetInventory.Vowels.Contains(t.Base)))
        {
            return new AlphabetDetection(
                PhoneAlphabet.Arpabet, 1.0,
                "Contains stress digits on ARPABET vowels; IPA never writes digits.");
        }

        var inInventory = split.Count(t => t.Ok && ArpabetInventory.Contains(t.Base));
        var inventoryFraction = (double) inInventory / tokens.Count;

        if (inventoryFraction < 1.0)
        {
            // Some token is not an ARPABET symbol at all. ASCII-only and not ARPABET is most
            // likely IPA written with the ASCII-representable symbols - but that is an inference,
            // not evidence, so it stays below the threshold.
            return new AlphabetDetection(
                PhoneAlphabet.Ipa, 0.6,
                $"{tokens.Count - inInventory} of {tokens.Count} symbols are not ARPABET, but nothing "
                + "here is exclusively IPA either.");
        }

        var uppercaseFraction = (double) tokens.Count(t => t.Any(char.IsAsciiLetterUpper)) / tokens.Count;

        if (uppercaseFraction >= 0.5)
        {
            return new AlphabetDetection(
                PhoneAlphabet.Arpabet, 0.9,
                "All symbols are in the ARPABET inventory and most are uppercase.");
        }

        // Lowercase, entirely within the ARPABET inventory. Multi-letter tokens are the only
        // remaining hint - "ah", "ow", "hh" are ARPABET shapes that IPA would have tokenized
        // differently - but people write ARPABET lowercase often enough that this is a hint, not
        // a decision.
        var multiLetter = tokens.Count(t => t.Count(char.IsAsciiLetter) >= 2);

        return multiLetter > 0
            ? new AlphabetDetection(
                PhoneAlphabet.Arpabet, 0.7,
                "All symbols are in the ARPABET inventory and some are multi-letter, but they are "
                + "lowercase - ARPABET is conventionally uppercase, so this is not conclusive.")
            : new AlphabetDetection(
                null, 0.5,
                "Every symbol is valid as both IPA and ARPABET (p, b, t, k, m, n and friends are "
                + "shared). There is no evidence either way - the alphabet has to be stated.");
    }

    private static bool HasIpaOnlyCharacter(string token) =>
        token.Any(c => !char.IsAscii(c) || c is PrimaryStress or SecondaryStress or LengthMark);
}
