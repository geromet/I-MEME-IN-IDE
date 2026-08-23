using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Tests.Phonetics;

public class PhonemeFeatureTableCoverageTests
{
    /// <summary>
    /// The symbols a real dutch_cv alignment produced. 20 of these 40 were unknown before #31,
    /// accounting for 32% of all phone tokens in that corpus.
    /// </summary>
    private static readonly string[] DutchCorpusSymbols =
    [
        "a", "aː", "b", "c", "d", "eː", "f", "h", "iː", "j", "k", "l", "m", "n", "oː", "p", "r",
        "s", "t", "u", "uː", "v", "w", "x", "y", "yː", "z", "øː", "ŋ", "œ", "ɑ", "ɔ", "ɛ", "ɛ̈",
        "ɣ", "ɥ", "ɪ", "ʋ", "ʏ", "ʏ̈",
    ];

    [Fact]
    public void EveryPhoneInARealDutchCorpusIsModelled()
    {
        var coverage = PhonemeFeatureTable.CoverageOf(DutchCorpusSymbols);

        Assert.Empty(coverage.UnknownSymbols);
        Assert.Equal(100, coverage.KnownPercent);
    }

    /// <summary>
    /// The near-misses that made the gap invisible: these look like symbols any phonetic table
    /// would have, and were absent only because the en-US inventory spells them differently.
    /// </summary>
    [Theory]
    [InlineData("r")]   // the trill; the table had only the English approximant ɹ
    [InlineData("ɔ")]   // the table had only ɔː
    [InlineData("ɑ")]   // the table had only ɑː
    public void NearMissSymbolsAreModelled(string symbol)
    {
        Assert.True(PhonemeFeatureTable.TryGetFeature(symbol, out _));
    }

    [Fact]
    public void CombiningMarksFallBackToTheBaseVowel()
    {
        // "ɛ̈" is a centralized ɛ, not a different phoneme class.
        Assert.True(PhonemeFeatureTable.TryGetFeature("ɛ̈", out var centralized));
        PhonemeFeatureTable.TryGetFeature("ɛ", out var plain);

        Assert.Equal(plain, centralized);
    }

    [Fact]
    public void LengthVariantsFallBackToTheOtherLengthForm()
    {
        // A vowel present only in one length form is still recognisable in the other.
        Assert.True(PhonemeFeatureTable.TryGetFeature("ɜ", out _));
        Assert.True(PhonemeFeatureTable.TryGetFeature("ɐː", out _));
    }

    [Fact]
    public void FallbackDoesNotInventFeaturesForGenuinelyUnknownSymbols()
    {
        Assert.False(PhonemeFeatureTable.TryGetFeature("QQ", out _));
        Assert.False(PhonemeFeatureTable.TryGetFeature("ʘ", out _));
    }

    /// <summary>
    /// The #31 exit criterion: a real Dutch minimal-pair distinction must score closer than an
    /// unrelated pair. Before, both were a flat unknown-symbol penalty and therefore identical.
    /// </summary>
    [Fact]
    public void DutchMinimalPairsScoreCloserThanUnrelatedPhones()
    {
        // ɣ and x differ only in voicing; ɣ and iː are not even the same class.
        var minimalPair = PhonemeFeatureTable.SubstitutionCost("ɣ", "x", 1.0);
        var unrelated = PhonemeFeatureTable.SubstitutionCost("ɣ", "iː", 1.0);

        Assert.True(minimalPair < unrelated, $"ɣ/x scored {minimalPair}, ɣ/iː scored {unrelated}.");
    }

    [Fact]
    public void CoverageReportsUnknownSymbolsSoTheyCanBeNamed()
    {
        var coverage = PhonemeFeatureTable.CoverageOf(["h", "ɛ", "QQ", "QQ", "ʘ"]);

        Assert.Equal(5, coverage.TotalPhones);
        Assert.Equal(3, coverage.UnknownPhones);
        Assert.Equal(["QQ", "ʘ"], coverage.UnknownSymbols);
    }

    [Fact]
    public void CoverageOfAnEmptySequenceIsNotAFailure()
    {
        Assert.Equal(100, PhonemeFeatureTable.CoverageOf([]).KnownPercent);
    }
}
