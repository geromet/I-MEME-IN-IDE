using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Tests.Phonetics;

public class PhoneAlphabetConverterTests
{
    /// <summary>
    /// The failure #18 singles out as easy to get wrong: the stress digit is not decoration.
    /// AH0 and AH1 are *different vowels* in IPA, so naive digit-stripping loses the stress and
    /// produces the wrong vowel at the same time.
    /// </summary>
    [Fact]
    public void ToCanonical_MapsStressDependentVowelsToDifferentIpaSymbols()
    {
        var unstressed = PhoneAlphabetConverter.ToCanonical("AH0", PhoneAlphabet.Arpabet);
        var stressed = PhoneAlphabetConverter.ToCanonical("AH1", PhoneAlphabet.Arpabet);

        Assert.Equal("ə", unstressed.Symbol);
        Assert.Equal("ʌ", stressed.Symbol);
        Assert.NotEqual(unstressed.Symbol, stressed.Symbol);
    }

    [Fact]
    public void ToCanonical_MapsErByStressToo()
    {
        // butter (ɚ) vs bird (ɜː) - verified against espeak-ng en-us.
        Assert.Equal("ɚ", PhoneAlphabetConverter.ToCanonical("ER0", PhoneAlphabet.Arpabet).Symbol);
        Assert.Equal("ɜː", PhoneAlphabetConverter.ToCanonical("ER1", PhoneAlphabet.Arpabet).Symbol);
    }

    [Fact]
    public void ToCanonical_KeepsStressRatherThanDiscardingIt()
    {
        Assert.Equal(1, PhoneAlphabetConverter.ToCanonical("OW1", PhoneAlphabet.Arpabet).Stress);
        Assert.Equal(0, PhoneAlphabetConverter.ToCanonical("IH0", PhoneAlphabet.Arpabet).Stress);
        Assert.Equal(2, PhoneAlphabetConverter.ToCanonical("AE2", PhoneAlphabet.Arpabet).Stress);
    }

    [Fact]
    public void ToCanonical_StressIsNotFoldedIntoTheSymbol()
    {
        // The same vowel at two stress levels must compare equal as a symbol, or every match
        // spanning a stress change would be penalized for a difference that is prosodic.
        var a = PhoneAlphabetConverter.ToCanonical("IY1", PhoneAlphabet.Arpabet);
        var b = PhoneAlphabetConverter.ToCanonical("IY0", PhoneAlphabet.Arpabet);

        Assert.Equal(a.Symbol, b.Symbol);
        Assert.NotEqual(a.Stress, b.Stress);
    }

    [Theory]
    [InlineData("HH", "h")]
    [InlineData("CH", "tʃ")]
    [InlineData("JH", "dʒ")]
    [InlineData("NG", "ŋ")]
    [InlineData("TH", "θ")]
    [InlineData("DH", "ð")]
    [InlineData("R", "ɹ")]
    [InlineData("G", "ɡ")]
    [InlineData("Y", "j")]
    public void ToCanonical_MapsConsonantsThatDifferBetweenTheAlphabets(string arpabet, string ipa)
    {
        Assert.Equal(ipa, PhoneAlphabetConverter.ToCanonical(arpabet, PhoneAlphabet.Arpabet).Symbol);
    }

    /// <summary>
    /// The whole point of canonicalizing: an ARPABET-aligned corpus has to be reachable by an
    /// IPA query. "hello" through both alphabets must land on the same symbols.
    /// </summary>
    [Fact]
    public void ToCanonical_RoundTripsArpabetAndIpaToTheSameSequence()
    {
        var fromArpabet = PhoneAlphabetConverter
            .ToCanonical("HH AH0 L OW1".Split(' '), PhoneAlphabet.Arpabet)
            .Select(p => p.Symbol);

        var fromIpa = PhoneAlphabetConverter
            .ToCanonical("h ə l oʊ".Split(' '), PhoneAlphabet.Ipa)
            .Select(p => p.Symbol);

        Assert.Equal(fromIpa, fromArpabet);
    }

    [Fact]
    public void ToCanonical_LiftsStressMarksOutOfIpaSymbols()
    {
        var result = PhoneAlphabetConverter.ToCanonical("ˈoʊ", PhoneAlphabet.Ipa);

        Assert.Equal("oʊ", result.Symbol);
        Assert.Equal(1, result.Stress);
    }

    [Fact]
    public void ToCanonical_PassesUnknownSymbolsThroughRatherThanDroppingThem()
    {
        // Dropping would shorten the sequence and corrupt every alignment position after it.
        // An unknown symbol scores as unknown in the feature table, which is honest.
        Assert.Equal("QQ", PhoneAlphabetConverter.ToCanonical("QQ", PhoneAlphabet.Arpabet).Symbol);
    }

    /// <summary>
    /// Guards the specific silent-degradation trap: converting to a symbol PhonemeFeatureTable
    /// does not know charges UnknownSymbolCost instead of a real phonetic distance, which reads
    /// as "poor match quality" rather than as a bug. Every conversion target must be a symbol the
    /// matcher can actually reason about.
    /// </summary>
    [Fact]
    public void EveryArpabetSymbolConvertsToASymbolTheFeatureTableKnows()
    {
        var allArpabet = ArpabetInventory.Consonants
            .Concat(ArpabetInventory.Vowels.SelectMany(v => new[] { v + "0", v + "1", v + "2" }));

        foreach (var symbol in allArpabet)
        {
            var canonical = PhoneAlphabetConverter.ToCanonical(symbol, PhoneAlphabet.Arpabet).Symbol;

            Assert.True(
                PhonemeFeatureTable.TryGetFeature(canonical, out _),
                $"ARPABET '{symbol}' converts to '{canonical}', which PhonemeFeatureTable does not know.");
        }
    }
}
