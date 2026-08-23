using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Tests.Phonetics;

public class PhoneAlphabetDetectorTests
{
    [Theory]
    // Real espeak-ng en-us output for "massive", "thing", "judge".
    [InlineData("m æ s ɪ v")]
    [InlineData("θ ɪ ŋ")]
    [InlineData("dʒ ʌ dʒ")]
    public void Detect_IdentifiesIpaFromNonAsciiCharacters(string symbols)
    {
        var result = PhoneAlphabetDetector.Detect(symbols);

        Assert.Equal(PhoneAlphabet.Ipa, result.Alphabet);
        Assert.True(result.IsConfident);
    }

    [Fact]
    public void Detect_IdentifiesIpaFromStressAndLengthMarks()
    {
        // Wiktionary-style input, which is what a user pastes - unlike stored espeak output, it
        // still carries the marks.
        var result = PhoneAlphabetDetector.Detect("h ə ˈl oʊ");

        Assert.Equal(PhoneAlphabet.Ipa, result.Alphabet);
        Assert.True(result.IsConfident);
    }

    [Fact]
    public void Detect_IdentifiesArpabetFromStressDigits()
    {
        // CMUdict / MFA english_us_arpa shape. Digits are decisive: IPA never writes them.
        var result = PhoneAlphabetDetector.Detect("HH AH0 L OW1");

        Assert.Equal(PhoneAlphabet.Arpabet, result.Alphabet);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void Detect_IdentifiesArpabetFromUppercaseInventoryWithoutDigits()
    {
        var result = PhoneAlphabetDetector.Detect("HH AH L OW");

        Assert.Equal(PhoneAlphabet.Arpabet, result.Alphabet);
        Assert.True(result.IsConfident);
    }

    /// <summary>
    /// The case the detector must not get wrong by guessing. Every one of these symbols is valid
    /// IPA *and* valid ARPABET-modulo-case, so there is genuinely no evidence - and a wrong answer
    /// here is silent, producing a query that simply never matches.
    /// </summary>
    [Fact]
    public void Detect_RefusesToChooseOnSymbolsSharedByBothAlphabets()
    {
        var result = PhoneAlphabetDetector.Detect("p b t d k m n");

        Assert.Null(result.Alphabet);
        Assert.False(result.IsConfident);
        Assert.Contains("has to be stated", result.Explanation);
    }

    [Fact]
    public void Detect_IsNotConfidentAboutLowercaseArpabet()
    {
        // People write ARPABET lowercase often enough that case alone cannot decide it.
        var result = PhoneAlphabetDetector.Detect("hh ah l ow");

        Assert.False(result.IsConfident);
    }

    [Fact]
    public void Detect_ReturnsNothingForEmptyInput()
    {
        Assert.Null(PhoneAlphabetDetector.Detect("").Alphabet);
        Assert.Null(PhoneAlphabetDetector.Detect([]).Alphabet);
    }

    /// <summary>
    /// The detector is used to validate provider declarations, so it must agree with what the real
    /// binary emits - not with an idealized notion of IPA.
    /// </summary>
    [Fact]
    public async Task Detect_AgreesWithRealEspeakOutput()
    {
        var locator = new Infrastructure.Processes.EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return;
        }

        var phonemizer = new Infrastructure.Phonetics.EspeakPhonemizer(locator);
        var result = await phonemizer.PhonemizeAsync("the quick brown fox jumps over the lazy dog", "en-US");

        var detection = PhoneAlphabetDetector.Detect(result.Words.SelectMany(w => w.Phonemes));

        Assert.Equal(phonemizer.Alphabet, detection.Alphabet);
        Assert.True(detection.IsConfident);
    }
}
