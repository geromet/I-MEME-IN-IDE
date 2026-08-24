using System.Diagnostics;
using System.Text.RegularExpressions;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Infrastructure.Alignment;

/// <summary>
/// IAlignmentProvider backed by the Montreal Forced Aligner CLI (addendum §26: optional/advanced,
/// not needed for ordinary transcript indexing). Unlike WhisperXAlignmentProvider, MFA genuinely
/// performs "align this exact given text to this audio" - it needs a corpus directory containing
/// the audio file alongside a .lab file holding the literal transcript, and aligns precisely that
/// text, not its own guess at what was said. This is also the phone-level alignment (addendum §6:
/// the Phone table can start sparse) that WhisperX-based alignment can't provide.
///
/// Requires a pretrained dictionary and acoustic model to already be available (via
/// `mfa model download acoustic english_us_arpa` / `mfa model download dictionary
/// english_us_arpa`) - MFA does not auto-download these on first use. Language is hardcoded to
/// en-US for now, matching the rest of the project's current "en-US only" scope.
/// </summary>
public partial class MfaAlignmentProvider(
    [FromKeyedServices("mfa")] IExternalToolLocator toolLocator,
    ISettingsStore settings,
    MfaSettings mfaSettings) : IAlignmentProvider
{
    public string ProviderName => "mfa";

    /// <summary>
    /// MFA's alphabet is a property of the *model*, not of MFA (#18). This was hardcoded to
    /// ARPABET, which is true only for english_us_arpa - the one model whose name says so. The
    /// _mfa and _cv model families emit IPA, so aligning Dutch with dutch_cv produced IPA phones
    /// tagged ARPABET, and the detector correctly refused to store them.
    ///
    /// Derived from the model name because that is MFA's own naming convention, and validated by
    /// the detector on write - so a model that breaks the convention fails loudly rather than
    /// silently mis-tagging a corpus.
    /// </summary>
    public PhoneAlphabet? PhoneAlphabet =>
        settings.Get(mfaSettings.AcousticModelSetting).EndsWith("_arpa", StringComparison.OrdinalIgnoreCase)
            ? Core.Phonetics.PhoneAlphabet.Arpabet
            : Core.Phonetics.PhoneAlphabet.Ipa;

    public async Task<AlignmentResult> AlignAsync(
        string mediaPath, IReadOnlyList<AlignmentUtterance> utterances, double totalDurationSeconds, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"mfa is not available: {status.Error}");
        }

        // MFA does not download models on first use, so the default state after installing it is
        // "no models" - and that failure only surfaces deep inside MFA. Check first, and say what
        // to install.
        if (mfaSettings.Validate(settings) is { } settingsError)
        {
            throw new InvalidOperationException(settingsError);
        }

        var corpusDir = Directory.CreateTempSubdirectory("memesearcher-mfa-corpus-").FullName;
        var outputDir = Directory.CreateTempSubdirectory("memesearcher-mfa-output-").FullName;
        try
        {
            var baseName = Path.GetFileNameWithoutExtension(mediaPath);
            var corpusMediaPath = Path.Combine(corpusDir, baseName + Path.GetExtension(mediaPath));
            var corpusTextGridPath = Path.Combine(corpusDir, baseName + ".TextGrid");

            LinkOrCopy(mediaPath, corpusMediaPath);

            // #33: a TextGrid-corpus input, segmented into the transcript's own utterances against
            // the intact audio file - not one `.lab` covering the whole recording as a single
            // utterance. A multi-thousand-word single "utterance" gave MFA's beam search one
            // enormous path with no intermediate anchors, so one unrecoverable stretch anywhere in
            // a 15-minute file failed the entire file rather than just that utterance.
            var textGrid = MfaUtteranceCorpusWriter.Write(utterances, totalDurationSeconds);
            await File.WriteAllTextAsync(corpusTextGridPath, textGrid, cancellationToken);

            await RunMfaAsync(
                status,
                corpusDir,
                outputDir,
                dictionary: settings.Get(mfaSettings.DictionarySetting),
                acousticModel: settings.Get(mfaSettings.AcousticModelSetting),
                cancellationToken);

            var textGridPath = Path.Combine(outputDir, baseName + ".TextGrid");
            if (!File.Exists(textGridPath))
            {
                throw new InvalidOperationException($"mfa did not produce the expected output file '{textGridPath}'.");
            }

            var content = await File.ReadAllTextAsync(textGridPath, cancellationToken);
            return ParseAlignmentResult(content);
        }
        finally
        {
            TryDelete(corpusDir);
            TryDelete(outputDir);
        }
    }

    /// <summary>
    /// Extracts word/phone timing from an MFA TextGrid's standard tier layout (tier "words",
    /// tier "phones"). Silence/gap intervals (empty text, or MFA's "sil"/"sp"/"spn" markers) are
    /// dropped rather than treated as real words or phones.
    /// </summary>
    public static AlignmentResult ParseAlignmentResult(string textGridContent)
    {
        var tiers = TextGridParser.Parse(textGridContent);

        var words = FindTier(tiers, "words")
            .Where(i => !string.IsNullOrWhiteSpace(i.Text))
            .Select(i => new AlignedWord(i.Text, i.StartSeconds, i.EndSeconds))
            .ToList();

        var phoneTier = FindTier(tiers, "phones");
        IReadOnlyList<AlignedPhone>? phones = phoneTier.Count > 0
            ? phoneTier
                .Where(i => !IsSilenceMarker(i.Text))
                .Select(i => new AlignedPhone(i.Text, i.StartSeconds, i.EndSeconds))
                .ToList()
            : null;

        return new AlignmentResult(words, phones);
    }

    /// <summary>
    /// #33: aligning against a TextGrid-corpus input (instead of a `.lab`) makes MFA treat the
    /// input tier's name as a speaker label, and a speaker-labelled corpus's output tiers are
    /// named "&lt;speaker&gt; - words"/"&lt;speaker&gt; - phones" rather than the bare "words"/
    /// "phones" a single `.lab` file produced. Matched by suffix (and the bare name, for a
    /// `.lab`-style corpus) rather than hardcoding the exact prefix MFA would use for our fixed
    /// "utterances" tier name, since that convention isn't verifiable without a real MFA install.
    /// </summary>
    private static List<TextGridParser.Interval> FindTier(
        IReadOnlyDictionary<string, List<TextGridParser.Interval>> tiers, string tierName)
    {
        var key = tiers.Keys.FirstOrDefault(k =>
            k.Equals(tierName, StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith(" - " + tierName, StringComparison.OrdinalIgnoreCase));

        return key is not null ? tiers[key] : [];
    }

    private static bool IsSilenceMarker(string text) =>
        string.IsNullOrWhiteSpace(text) || text is "sil" or "sp" or "spn";

    private static void LinkOrCopy(string source, string destination)
    {
        try
        {
            File.CreateSymbolicLink(destination, source);
        }
        catch
        {
            // Symlinks can fail for all sorts of platform/filesystem/permission reasons - a plain
            // copy always works, just costs more disk I/O for large media.
            File.Copy(source, destination);
        }
    }

    private static async Task RunMfaAsync(
        ExternalToolStatus status,
        string corpusDir,
        string outputDir,
        string dictionary,
        string acousticModel,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(status.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("align");
        startInfo.ArgumentList.Add(corpusDir);
        // `mfa align CORPUS DICTIONARY ACOUSTIC OUTPUT` - the dictionary comes first. This was
        // one constant passed twice before, so the order never mattered; with two independent
        // settings it does, and swapping them produces a confusing model-not-found for whichever
        // name lands in the wrong slot.
        startInfo.ArgumentList.Add(dictionary);
        startInfo.ArgumentList.Add(acousticModel);
        startInfo.ArgumentList.Add(outputDir);
        startInfo.ArgumentList.Add("--clean");

        using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
            ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

        // Both streams must be drained concurrently with waiting for exit, not just stderr - a
        // long alignment run (mfa's own progress/summary output) can write more than a pipe's OS
        // buffer holds, and with nobody reading stdout, mfa blocks on that write() forever while
        // this method blocks forever on WaitForExitAsync. Observed directly: a real 19-minute
        // recording finished its actual alignment work (per mfa's own log) within a minute, then
        // the process sat idle for over an hour before this fix, doing nothing but waiting on a
        // full stdout pipe nobody was reading.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cancellationToken);
        await stdoutTask; // discarded - only draining the pipe matters, not its content.

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException(
                $"mfa exited with code {process.ExitCode}: {SummarizeMfaError(stderr)}");
        }
    }

    /// <summary>
    /// Reduces MFA's error output to the sentence that matters.
    ///
    /// MFA renders errors as a Unicode box - border characters, padding, blank lines - which is
    /// fine in a terminal and unreadable anywhere else. Passed through verbatim it becomes dozens
    /// of lines of box-drawing in a one-line status bar, which is how a perfectly clear
    /// "Could not find a model named ..." ends up looking like nothing happened at all.
    /// </summary>
    public static string SummarizeMfaError(string stderr)
    {
        var lines = stderr.Split('\n').Select(line => line.TrimEnd()).ToList();

        // MFA prints a usage banner *and* an error box. Only the box says what went wrong, so
        // find it rather than concatenating everything - the banner is longer than the message
        // and would bury it.
        var boxStart = lines.FindIndex(line => line.TrimStart().StartsWith('\u256d') && line.Contains("Error"));

        if (boxStart >= 0)
        {
            var boxed = lines
                .Skip(boxStart + 1)
                .TakeWhile(line => !line.TrimStart().StartsWith('\u2570'))
                .Select(StripBorders)
                .Where(line => line.Length > 0);

            var message = string.Join(" ", boxed);
            if (message.Length > 0)
            {
                return message;
            }
        }

        // #33: a plain Python traceback (no box) has no border to find - MFA's NoAlignmentsError
        // and friends print this way, and the whole ~2KB traceback was passing through verbatim
        // before this, with the actual "NoAlignmentsError: ..." sentence buried at the very end.
        // A traceback's final exception always looks like "SomeError: message", optionally
        // wrapping onto further unindented lines with no blank line in between - both are kept,
        // everything before the exception line (the call stack) is dropped.
        var exceptionLineIndex = lines.FindLastIndex(line => ExceptionLine().IsMatch(line.TrimStart()));

        if (exceptionLineIndex >= 0)
        {
            var tail = lines
                .Skip(exceptionLineIndex)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);

            return string.Join(" ", tail);
        }

        var fallback = lines.Select(StripBorders).Where(line => line.Length > 0).ToList();

        return fallback.Count > 0 ? fallback[^1] : "no error output";
    }

    [GeneratedRegex(@"^[A-Za-z_][\w.]*(Error|Exception)\s*:\s*.+")]
    private static partial Regex ExceptionLine();

    private static string StripBorders(string line) =>
        line.Trim().Trim('\u2502', '\u256d', '\u2570', '\u256e', '\u256f', '\u2500').Trim();

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
