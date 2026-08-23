using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;

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
public class MfaAlignmentProvider(MfaToolLocator toolLocator) : IAlignmentProvider
{
    public string ProviderName => "mfa";

    private const string DictionaryAndAcousticModel = "english_us_arpa";

    public async Task<AlignmentResult> AlignAsync(string mediaPath, string transcriptText, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"mfa is not available: {status.Error}");
        }

        var corpusDir = Directory.CreateTempSubdirectory("memesearcher-mfa-corpus-").FullName;
        var outputDir = Directory.CreateTempSubdirectory("memesearcher-mfa-output-").FullName;
        try
        {
            var baseName = Path.GetFileNameWithoutExtension(mediaPath);
            var corpusMediaPath = Path.Combine(corpusDir, baseName + Path.GetExtension(mediaPath));
            var corpusLabPath = Path.Combine(corpusDir, baseName + ".lab");

            LinkOrCopy(mediaPath, corpusMediaPath);
            await File.WriteAllTextAsync(corpusLabPath, transcriptText, cancellationToken);

            await RunMfaAsync(status.ExecutablePath!, corpusDir, outputDir, cancellationToken);

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

        var words = tiers.GetValueOrDefault("words", [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Text))
            .Select(i => new AlignedWord(i.Text, i.StartSeconds, i.EndSeconds))
            .ToList();

        var phoneTier = tiers.GetValueOrDefault("phones", []);
        IReadOnlyList<AlignedPhone>? phones = phoneTier.Count > 0
            ? phoneTier
                .Where(i => !IsSilenceMarker(i.Text))
                .Select(i => new AlignedPhone(i.Text, i.StartSeconds, i.EndSeconds))
                .ToList()
            : null;

        return new AlignmentResult(words, phones);
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

    private static async Task RunMfaAsync(string mfaPath, string corpusDir, string outputDir, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(mfaPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("align");
        startInfo.ArgumentList.Add(corpusDir);
        startInfo.ArgumentList.Add(DictionaryAndAcousticModel);
        startInfo.ArgumentList.Add(DictionaryAndAcousticModel);
        startInfo.ArgumentList.Add(outputDir);
        startInfo.ArgumentList.Add("--clean");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{mfaPath}'.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"mfa exited with code {process.ExitCode}: {stderr}");
        }
    }

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
