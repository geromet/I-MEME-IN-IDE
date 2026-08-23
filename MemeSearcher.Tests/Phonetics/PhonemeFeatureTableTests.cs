using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Tests.Phonetics;

public class PhonemeFeatureTableTests
{
    [Fact]
    public void SubstitutionCost_IdenticalSymbolsAreFree()
    {
        Assert.Equal(0, PhonemeFeatureTable.SubstitutionCost("s", "s", maxCost: 1.0));
    }

    [Theory]
    [InlineData("s", "z")] // voicing only
    [InlineData("t", "d")]
    [InlineData("k", "ɡ")]
    [InlineData("f", "v")]
    public void SubstitutionCost_VoicingPairsAreVeryClose(string a, string b)
    {
        var cost = PhonemeFeatureTable.SubstitutionCost(a, b, maxCost: 1.0);
        Assert.True(cost < 0.3, $"expected {a}/{b} to be very close, got {cost}");
    }

    [Fact]
    public void SubstitutionCost_DifferentPlaceAndMannerIsSubstantiallyHigherThanVoicingPair()
    {
        var close = PhonemeFeatureTable.SubstitutionCost("s", "z", maxCost: 1.0);
        var distant = PhonemeFeatureTable.SubstitutionCost("s", "m", maxCost: 1.0);

        Assert.True(distant > close, $"expected s/m ({distant}) > s/z ({close})");
    }

    [Fact]
    public void SubstitutionCost_CrossingVowelConsonantIsWorseThanAnyIntraClassPair()
    {
        var consonantToConsonant = PhonemeFeatureTable.SubstitutionCost("s", "m", maxCost: 1.0);
        var consonantToVowel = PhonemeFeatureTable.SubstitutionCost("s", "æ", maxCost: 1.0);

        Assert.True(consonantToVowel > consonantToConsonant);
    }

    [Fact]
    public void SubstitutionCost_UnknownSymbolsAreCostlyButNotInfinite()
    {
        var cost = PhonemeFeatureTable.SubstitutionCost("ʔ", "s", maxCost: 1.0);

        Assert.True(cost is > 0 and < double.PositiveInfinity);
    }

    [Fact]
    public void SubstitutionCost_ScalesLinearlyWithMaxCost()
    {
        var atOne = PhonemeFeatureTable.SubstitutionCost("s", "m", maxCost: 1.0);
        var atTwo = PhonemeFeatureTable.SubstitutionCost("s", "m", maxCost: 2.0);

        Assert.Equal(atOne * 2, atTwo, precision: 6);
    }
}
