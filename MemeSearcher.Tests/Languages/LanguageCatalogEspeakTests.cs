using MemeSearcher.Core.Languages;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Languages;

/// <summary>
/// Checks the catalog's espeak voice names against the real espeak-ng, in the same spirit as
/// EspeakPhonemizerTests: the risk is entirely in "is this string the one the tool actually
/// wants", which no mock can answer. Skips when espeak-ng is not installed.
///
/// There is no equivalent test for the whisper side. `whisperx --help` loads torch before it will
/// print its accepted `--language` values, which is far too slow for a unit test; the shape
/// constraint in LanguageCatalogTests.EveryWhisperCodeIsATwoLetterCode is the cheap stand-in.
/// </summary>
public class LanguageCatalogEspeakTests
{
    [Fact]
    public async Task EveryCatalogVoiceIsAcceptedByEspeak()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return;
        }

        var phonemizer = new EspeakPhonemizer(locator);

        foreach (var option in LanguageCatalog.All)
        {
            var result = await phonemizer.PhonemizeAsync("test", option.Id);

            Assert.False(
                string.IsNullOrWhiteSpace(result.Ipa),
                $"espeak-ng produced no IPA for '{option.Id}' (voice '{option.EspeakVoice}').");
        }
    }
}
