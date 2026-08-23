using MemeSearcher.Core.Languages;

namespace MemeSearcher.Tests.Languages;

public class LanguageCatalogTests
{
    [Fact]
    public void Get_ResolvesNeutralIdToBothToolCodes()
    {
        var dutch = LanguageCatalog.Get("nl");

        Assert.Equal("nl", dutch.EspeakVoice);
        Assert.Equal("nl", dutch.WhisperCode);
    }

    /// <summary>
    /// The regression this whole type exists for (#23): the two tools disagree about how English
    /// is named, and the previous single-string design could only ever satisfy one of them.
    /// whisperx rejects region-qualified tags outright via argparse `choices`, so "en-US" reaching
    /// it killed the import before any audio was read.
    /// </summary>
    [Fact]
    public void Get_StripsRegionForWhisperButKeepsItForEspeak()
    {
        var american = LanguageCatalog.Get("en-US");
        var british = LanguageCatalog.Get("en-GB");

        Assert.Equal("en", american.WhisperCode);
        Assert.Equal("en", british.WhisperCode);

        // espeak's region suffix is not decoration - these are different voices producing
        // different IPA, so collapsing them the way whisperx does would lose real information.
        Assert.Equal("en-us", american.EspeakVoice);
        Assert.Equal("en-gb", british.EspeakVoice);
        Assert.NotEqual(american.EspeakVoice, british.EspeakVoice);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        Assert.Equal(LanguageCatalog.Get("en-US"), LanguageCatalog.Get("en-us"));
    }

    [Fact]
    public void Get_ThrowsWithTheSupportedSetForAnUnknownId()
    {
        var ex = Assert.Throws<UnsupportedLanguageException>(() => LanguageCatalog.Get("kl-KL"));

        Assert.Equal("kl-KL", ex.LanguageId);
        Assert.Contains("en-US", ex.Message);
    }

    [Fact]
    public void TryGet_ReportsFailureWithoutThrowing()
    {
        Assert.False(LanguageCatalog.TryGet("kl-KL", out _));
        Assert.False(LanguageCatalog.TryGet(null, out _));
        Assert.True(LanguageCatalog.TryGet("de", out var german));
        Assert.Equal("de", german.Id);
    }

    /// <summary>
    /// Every whisper code must be a bare ISO 639-1 two-letter code, because that is all whisperx
    /// accepts. A new catalog entry that copies a region-qualified id into WhisperCode would
    /// reintroduce #23 for that language only, which is exactly the kind of bug that hides.
    /// </summary>
    [Fact]
    public void EveryWhisperCodeIsATwoLetterCode()
    {
        Assert.All(LanguageCatalog.All, option =>
        {
            Assert.Equal(2, option.WhisperCode.Length);
            Assert.Equal(option.WhisperCode.ToLowerInvariant(), option.WhisperCode);
        });
    }

    [Fact]
    public void IdsAreUnique()
    {
        Assert.Equal(
            LanguageCatalog.All.Count,
            LanguageCatalog.All.Select(o => o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
