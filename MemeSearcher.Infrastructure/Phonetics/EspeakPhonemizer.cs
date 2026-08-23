using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Languages;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Infrastructure.Phonetics;

/// <summary>
/// Wraps the `espeak-ng` CLI directly (one process invocation per call, text piped over stdin,
/// phonemes read back over stdout with `--sep=_`). Deliberately not a Python `phonemizer` worker -
/// eSpeak NG already produces IPA on its own, so introducing a Python interpreter and a
/// stdin/stdout JSON worker protocol just to reach the same backend would add a dependency for no
/// benefit. If per-call process-spawn overhead ever becomes a bottleneck, a persistent worker
/// process is the natural next step - not Python.
/// </summary>
public class EspeakPhonemizer(IExternalToolLocator toolLocator) : IPhonemizer
{
    public string ProviderName => "espeak-ng";

    // espeak-ng is invoked with --ipa, so this is known rather than inferred (#18).
    public PhoneAlphabet Alphabet => PhoneAlphabet.Ipa;

    // The neutral ids from LanguageCatalog, not espeak voice names - callers pass ids and this
    // class maps to a voice at invocation time (#23). espeak-ng supports far more voices than
    // this; the catalog is deliberately limited to languages whisperx also supports, since both
    // halves of the pipeline have to work for a language to be usable.
    public IReadOnlyCollection<string> SupportedLanguages => LanguageCatalog.SupportedIds;

    public async Task<PhonemizationResult> PhonemizeAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        var words = TextNormalizer.Tokenize(TextNormalizer.Normalize(text));
        if (words.Length == 0)
        {
            return new PhonemizationResult(text, "", []);
        }

        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"espeak-ng is not available: {status.Error}");
        }

        // Resolve before spawning: an unknown id here is a clear error, whereas an unknown voice
        // name reaches espeak-ng and comes back as a silent fallback to the default voice.
        var voice = LanguageCatalog.Get(language).EspeakVoice;

        var wordGroups = await RunEspeakAsync(status.ExecutablePath!, voice, string.Join(' ', words), cancellationToken);

        var phonemizedWords = BuildPhonemizedWords(words, wordGroups);

        var fullIpa = string.Join(' ', phonemizedWords.Select(w => w.Ipa));
        return new PhonemizationResult(text, fullIpa, phonemizedWords);
    }

    private static List<PhonemizedWord> BuildPhonemizedWords(string[] words, string[] wordGroups)
    {
        var result = new List<PhonemizedWord>(words.Length);

        for (var i = 0; i < words.Length; i++)
        {
            var group = i < wordGroups.Length ? wordGroups[i] : "";
            result.Add(new PhonemizedWord(words[i], ToDisplayIpa(group), IpaTokenizer.TokenizeWordGroup(group)));
        }

        // espeak can expand a single written token into multiple spoken words (numbers, some
        // abbreviations), producing more groups than input words. Rather than silently dropping
        // that phoneme data, fold the extras onto the last word.
        if (wordGroups.Length > words.Length && result.Count > 0)
        {
            var extraGroups = wordGroups.Skip(words.Length).ToList();
            var last = result[^1];

            var mergedPhonemes = last.Phonemes
                .Concat(extraGroups.SelectMany(IpaTokenizer.TokenizeWordGroup))
                .ToList();
            var mergedIpa = last.Ipa + string.Concat(extraGroups.Select(ToDisplayIpa));

            result[^1] = last with { Ipa = mergedIpa, Phonemes = mergedPhonemes };
        }

        return result;
    }

    private static string ToDisplayIpa(string sepDelimitedGroup) => sepDelimitedGroup.Replace("_", "");

    private static async Task<string[]> RunEspeakAsync(
        string executablePath,
        string voice,
        string inputLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add(voice);
        startInfo.ArgumentList.Add("--ipa");
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("--sep=_");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

        await process.StandardInput.WriteLineAsync(inputLine);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"espeak-ng exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.Split((char[]) [' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }
}
