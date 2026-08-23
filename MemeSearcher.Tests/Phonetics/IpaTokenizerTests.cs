using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Tests.Phonetics;

public class IpaTokenizerTests
{
    [Fact]
    public void TokenizeWordGroup_SplitsOnUnderscoreAndStripsStress()
    {
        var phonemes = IpaTokenizer.TokenizeWordGroup("m_ˈæ_s_ɪ_v");

        Assert.Equal(["m", "æ", "s", "ɪ", "v"], phonemes);
    }

    [Fact]
    public void TokenizeWordGroup_KeepsMultiCodepointPhonemesIntact()
    {
        // Affricate "dʒ" and long vowel "ɑː" must stay single tokens, not be split further.
        var phonemes = IpaTokenizer.TokenizeWordGroup("dʒ_ˈʌ_dʒ");
        Assert.Equal(["dʒ", "ʌ", "dʒ"], phonemes);

        var longVowel = IpaTokenizer.TokenizeWordGroup("k_ˈɑː");
        Assert.Equal(["k", "ɑː"], longVowel);
    }

    [Fact]
    public void TokenizeWordGroup_SecondaryStressIsAlsoStripped()
    {
        var phonemes = IpaTokenizer.TokenizeWordGroup("ˌʌ_s");
        Assert.Equal(["ʌ", "s"], phonemes);
    }

    [Fact]
    public void TokenizeWordGroup_EmptyInputReturnsNoPhonemes()
    {
        Assert.Empty(IpaTokenizer.TokenizeWordGroup(""));
    }
}
