using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Phonetics;

/// <summary>
/// Exercises the real espeak-ng binary rather than mocking the process boundary - the risk in
/// this component is entirely in "does espeak-ng's actual CLI behave the way we assumed", which
/// a mock can't catch. Skips (returns early) if espeak-ng isn't installed on the machine running
/// the tests, since it's a system dependency per handoff §35, not something the test suite bundles.
/// </summary>
public class EspeakPhonemizerTests
{
    private static async Task<EspeakPhonemizer?> CreatePhonemizerIfAvailableAsync()
    {
        var locator = new EspeakToolLocator();
        var status = await locator.LocateAsync();
        return status.IsInstalled ? new EspeakPhonemizer(locator) : null;
    }

    [Fact]
    public async Task PhonemizeAsync_ProducesPerWordPhonemesAndIpa()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        var result = await phonemizer.PhonemizeAsync("among us", "en-us");

        Assert.Equal(2, result.Words.Count);
        Assert.Equal("among", result.Words[0].Text);
        Assert.Equal("us", result.Words[1].Text);
        Assert.NotEmpty(result.Words[0].Phonemes);
        Assert.NotEmpty(result.Words[1].Phonemes);
        Assert.All(result.Words.SelectMany(w => w.Phonemes), phoneme => Assert.DoesNotContain('_', phoneme));
        Assert.False(string.IsNullOrWhiteSpace(result.Ipa));
    }

    [Fact]
    public async Task PhonemizeAsync_StripsPunctuationBeforeSendingToEspeak()
    {
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        var result = await phonemizer.PhonemizeAsync("Hello, world!", "en-us");

        Assert.Equal(["hello", "world"], result.Words.Select(w => w.Text));
    }

    [Fact]
    public async Task PhonemizeAsync_EmptyQueryReturnsEmptyResultWithoutInvokingEspeak()
    {
        // Doesn't need espeak-ng installed - normalization short-circuits before the process runs.
        var phonemizer = new EspeakPhonemizer(new EspeakToolLocator());

        var result = await phonemizer.PhonemizeAsync("   ", "en-us");

        Assert.Empty(result.Words);
        Assert.Equal("", result.Ipa);
    }

    [Fact]
    public async Task PhonemizeAsync_ArbitraryNonDictionaryStringStillProducesPhonemes()
    {
        // handoff §40/§41: the query does not have to be a real word.
        var phonemizer = await CreatePhonemizerIfAvailableAsync();
        if (phonemizer is null)
        {
            return;
        }

        var result = await phonemizer.PhonemizeAsync("zzyzx blorp", "en-us");

        Assert.Equal(2, result.Words.Count);
        Assert.NotEmpty(result.Words[0].Phonemes);
        Assert.NotEmpty(result.Words[1].Phonemes);
    }
}
