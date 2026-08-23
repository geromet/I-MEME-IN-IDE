namespace MemeSearcher.Core.Phonetics;

public enum ConsonantPlace { Bilabial, Labiodental, Dental, Alveolar, PostAlveolar, Palatal, Velar, Glottal }

public enum ConsonantManner { Stop, Affricate, Fricative, Nasal, Flap, Liquid, Glide }

public enum VowelHeight { High, MidHigh, Mid, MidLow, Low }

public enum VowelBackness { Front, Central, Back }

/// <summary>
/// A phonetic feature vector for one phoneme symbol, used only to compute search-heuristic
/// distances (handoff §14: "these are search heuristics, not claims about universal phonological
/// equivalence"). Built against espeak-ng's actual en-us `--ipa --sep=_` output - confirmed
/// empirically (e.g. the script-g "ɡ" not ascii "g", "ɹ" not "r" for the rhotic approximant, the
/// flap "ɾ" that shows up for intervocalic t/d, rhotic schwa "ɚ") rather than idealized IPA.
/// </summary>
public readonly record struct PhonemeFeature
{
    public required bool IsVowel { get; init; }

    // Consonant features.
    public ConsonantPlace Place { get; init; }
    public ConsonantManner Manner { get; init; }
    public bool Voiced { get; init; }

    // Vowel features.
    public VowelHeight Height { get; init; }
    public VowelBackness Backness { get; init; }
    public bool Rounded { get; init; }
    public bool Long { get; init; }
    public bool IsDiphthong { get; init; }
}

public static class PhonemeFeatureTable
{
    private const double PlaceWeight = 0.4;
    private const double MannerWeight = 0.4;
    private const double VoicingWeight = 0.2;

    private const double HeightWeight = 0.4;
    private const double BacknessWeight = 0.3;
    private const double RoundedWeight = 0.15;
    private const double LengthWeight = 0.15;
    private const double DiphthongMismatchPenalty = 0.15;

    private const double CrossClassMultiplier = 1.25;
    private const double UnknownSymbolCost = 0.9;

    private static readonly Dictionary<string, PhonemeFeature> Features = new()
    {
        // Consonants.
        ["p"] = Consonant(ConsonantPlace.Bilabial, ConsonantManner.Stop, voiced: false),
        ["b"] = Consonant(ConsonantPlace.Bilabial, ConsonantManner.Stop, voiced: true),
        ["t"] = Consonant(ConsonantPlace.Alveolar, ConsonantManner.Stop, voiced: false),
        ["d"] = Consonant(ConsonantPlace.Alveolar, ConsonantManner.Stop, voiced: true),
        ["k"] = Consonant(ConsonantPlace.Velar, ConsonantManner.Stop, voiced: false),
        ["ɡ"] = Consonant(ConsonantPlace.Velar, ConsonantManner.Stop, voiced: true),
        ["tʃ"] = Consonant(ConsonantPlace.PostAlveolar, ConsonantManner.Affricate, voiced: false),
        ["dʒ"] = Consonant(ConsonantPlace.PostAlveolar, ConsonantManner.Affricate, voiced: true),
        ["f"] = Consonant(ConsonantPlace.Labiodental, ConsonantManner.Fricative, voiced: false),
        ["v"] = Consonant(ConsonantPlace.Labiodental, ConsonantManner.Fricative, voiced: true),
        ["θ"] = Consonant(ConsonantPlace.Dental, ConsonantManner.Fricative, voiced: false),
        ["ð"] = Consonant(ConsonantPlace.Dental, ConsonantManner.Fricative, voiced: true),
        ["s"] = Consonant(ConsonantPlace.Alveolar, ConsonantManner.Fricative, voiced: false),
        ["z"] = Consonant(ConsonantPlace.Alveolar, ConsonantManner.Fricative, voiced: true),
        ["ʃ"] = Consonant(ConsonantPlace.PostAlveolar, ConsonantManner.Fricative, voiced: false),
        ["ʒ"] = Consonant(ConsonantPlace.PostAlveolar, ConsonantManner.Fricative, voiced: true),
        ["h"] = Consonant(ConsonantPlace.Glottal, ConsonantManner.Fricative, voiced: false),
        ["m"] = Consonant(ConsonantPlace.Bilabial, ConsonantManner.Nasal, voiced: true),
        ["n"] = Consonant(ConsonantPlace.Alveolar, ConsonantManner.Nasal, voiced: true),
        ["ŋ"] = Consonant(ConsonantPlace.Velar, ConsonantManner.Nasal, voiced: true),
        ["l"] = Consonant(ConsonantPlace.Alveolar, ConsonantManner.Liquid, voiced: true),
        ["ɹ"] = Consonant(ConsonantPlace.PostAlveolar, ConsonantManner.Liquid, voiced: true),
        ["ɾ"] = Consonant(ConsonantPlace.Alveolar, ConsonantManner.Flap, voiced: true),
        ["j"] = Consonant(ConsonantPlace.Palatal, ConsonantManner.Glide, voiced: true),
        ["w"] = Consonant(ConsonantPlace.Bilabial, ConsonantManner.Glide, voiced: true),

        // Vowels (monophthongs).
        ["iː"] = Vowel(VowelHeight.High, VowelBackness.Front, rounded: false, isLong: true),
        ["ɪ"] = Vowel(VowelHeight.MidHigh, VowelBackness.Front, rounded: false, isLong: false),
        ["ɛ"] = Vowel(VowelHeight.MidLow, VowelBackness.Front, rounded: false, isLong: false),
        ["æ"] = Vowel(VowelHeight.MidLow, VowelBackness.Front, rounded: false, isLong: false),
        ["ɑː"] = Vowel(VowelHeight.Low, VowelBackness.Back, rounded: false, isLong: true),
        ["ɔː"] = Vowel(VowelHeight.MidLow, VowelBackness.Back, rounded: true, isLong: true),
        ["ʊ"] = Vowel(VowelHeight.MidHigh, VowelBackness.Back, rounded: true, isLong: false),
        ["uː"] = Vowel(VowelHeight.High, VowelBackness.Back, rounded: true, isLong: true),
        ["ʌ"] = Vowel(VowelHeight.Mid, VowelBackness.Back, rounded: false, isLong: false),
        ["ɜː"] = Vowel(VowelHeight.Mid, VowelBackness.Central, rounded: false, isLong: true),
        ["ə"] = Vowel(VowelHeight.Mid, VowelBackness.Central, rounded: false, isLong: false),
        ["ɚ"] = Vowel(VowelHeight.Mid, VowelBackness.Central, rounded: false, isLong: false),
        ["ɐ"] = Vowel(VowelHeight.Low, VowelBackness.Central, rounded: false, isLong: false),

        // Diphthongs - approximated by onset quality (handoff §14: heuristic, not a phonology claim).
        ["eɪ"] = Vowel(VowelHeight.MidHigh, VowelBackness.Front, rounded: false, isLong: false, isDiphthong: true),
        ["aɪ"] = Vowel(VowelHeight.Low, VowelBackness.Front, rounded: false, isLong: false, isDiphthong: true),
        ["ɔɪ"] = Vowel(VowelHeight.MidLow, VowelBackness.Back, rounded: true, isLong: false, isDiphthong: true),
        ["aʊ"] = Vowel(VowelHeight.Low, VowelBackness.Central, rounded: false, isLong: false, isDiphthong: true),
        ["oʊ"] = Vowel(VowelHeight.MidHigh, VowelBackness.Back, rounded: true, isLong: false, isDiphthong: true),
    };

    private static PhonemeFeature Consonant(ConsonantPlace place, ConsonantManner manner, bool voiced) =>
        new() { IsVowel = false, Place = place, Manner = manner, Voiced = voiced };

    private static PhonemeFeature Vowel(VowelHeight height, VowelBackness backness, bool rounded, bool isLong, bool isDiphthong = false) =>
        new() { IsVowel = true, Height = height, Backness = backness, Rounded = rounded, Long = isLong, IsDiphthong = isDiphthong };

    public static bool TryGetFeature(string symbol, out PhonemeFeature feature) => Features.TryGetValue(symbol, out feature);

    /// <summary>
    /// Search-heuristic distance between two phoneme symbols, scaled to [0, maxCost].
    /// Identical symbols always cost 0, regardless of table membership.
    /// </summary>
    public static double SubstitutionCost(string a, string b, double maxCost)
    {
        if (a == b)
        {
            return 0;
        }

        var haveA = Features.TryGetValue(a, out var featureA);
        var haveB = Features.TryGetValue(b, out var featureB);

        if (!haveA || !haveB)
        {
            return UnknownSymbolCost * maxCost;
        }

        if (featureA.IsVowel != featureB.IsVowel)
        {
            return CrossClassMultiplier * maxCost;
        }

        return featureA.IsVowel
            ? VowelDistance(featureA, featureB) * maxCost
            : ConsonantDistance(featureA, featureB) * maxCost;
    }

    private static double ConsonantDistance(PhonemeFeature a, PhonemeFeature b)
    {
        var placeDistance = Math.Abs((int)a.Place - (int)b.Place) / 7.0;
        var mannerDistance = Math.Abs((int)a.Manner - (int)b.Manner) / 6.0;
        var voicingDistance = a.Voiced == b.Voiced ? 0.0 : 1.0;

        return PlaceWeight * placeDistance + MannerWeight * mannerDistance + VoicingWeight * voicingDistance;
    }

    private static double VowelDistance(PhonemeFeature a, PhonemeFeature b)
    {
        var heightDistance = Math.Abs((int)a.Height - (int)b.Height) / 4.0;
        var backnessDistance = Math.Abs((int)a.Backness - (int)b.Backness) / 2.0;
        var roundedDistance = a.Rounded == b.Rounded ? 0.0 : 1.0;
        var lengthDistance = a.Long == b.Long ? 0.0 : 1.0;
        var diphthongPenalty = a.IsDiphthong != b.IsDiphthong ? DiphthongMismatchPenalty : 0.0;

        var distance = HeightWeight * heightDistance + BacknessWeight * backnessDistance
            + RoundedWeight * roundedDistance + LengthWeight * lengthDistance + diphthongPenalty;

        return Math.Min(distance, 1.0);
    }
}
