namespace MemeSearcher.Core.Phonetics;

/// <summary>
/// Which notation a stored phone symbol is written in (#18).
///
/// This exists because two alphabets were already coexisting untagged: espeak-ng writes IPA into
/// <c>Word.PhonemeSequence</c> while MFA writes ARPABET into <c>Phone.Symbol</c>, so after a
/// realignment a single Word carries both at once. Nothing recorded which was which, and
/// <see cref="PhonemeFeatureTable"/> fed the wrong one returns plausible-looking costs rather than
/// erroring - the failure reads as "poor match quality", not as a bug.
///
/// <see cref="Ipa"/> is canonical: it is what PhonemeFeatureTable and every existing test are
/// built against, and it is language-neutral. Everything else converts into it for matching.
/// </summary>
public enum PhoneAlphabet
{
    /// <summary>espeak-ng `--ipa` output. The canonical form for matching and indexing.</summary>
    Ipa,

    /// <summary>ARPABET with stress digits, as emitted by MFA's english_us_arpa models (HH, AH0, OW1).</summary>
    Arpabet,
}
