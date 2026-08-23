using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Infrastructure.Transcription;

/// <summary>
/// Standalone IAlignmentProvider (handoff §25 names this as a distinct class from
/// WhisperXTranscriptionProvider, even though both wrap the same underlying tool), for the case
/// where a transcript came from somewhere else entirely - an imported SRT, say - and needs real
/// per-word timing without re-transcribing it.
///
/// Important fidelity caveat, stated plainly rather than glossed over: the plain `whisperx` CLI
/// doesn't expose "align this exact given text to this audio" as a distinct operation - only
/// "transcribe (and align) this audio." So this runs whisperx's own transcription+alignment and
/// returns *its* word-level output as the alignment result. When the given transcriptText is
/// whisperx's own prior output (e.g. re-aligning after a model upgrade), that's exactly right.
/// When transcriptText came from an independent source (a hand-written or downloaded SRT),
/// whisperx's transcribed wording may not exactly match it, and this class does not attempt to
/// force a match - callers should treat the result as "what whisperx heard," not a guaranteed
/// alignment of the literal input text, and fall back to interpolation if the word count or
/// content diverges more than expected (mirroring MediaIngestionService.BuildWords's existing
/// count-mismatch fallback). A true "align given text" operation would need MFA (Milestone 6) or
/// whisperx's Python API rather than its CLI.
/// </summary>
public class WhisperXAlignmentProvider(WhisperXToolLocator toolLocator) : IAlignmentProvider
{
    public string ProviderName => "whisperx";

    // Word-level alignment only - it produces no phones, so it has no phone alphabet (#18).
    public PhoneAlphabet? PhoneAlphabet => null;

    public async Task<AlignmentResult> AlignAsync(string mediaPath, string transcriptText, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"whisperx is not available: {status.Error}");
        }

        var outputDir = Directory.CreateTempSubdirectory("memesearcher-whisperx-align-").FullName;
        try
        {
            await RunWhisperXAsync(status.ExecutablePath!, mediaPath, outputDir, cancellationToken);

            var outputJsonPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(mediaPath) + ".json");
            if (!File.Exists(outputJsonPath))
            {
                throw new InvalidOperationException(
                    $"whisperx did not produce the expected output file '{outputJsonPath}'.");
            }

            var json = await File.ReadAllTextAsync(outputJsonPath, cancellationToken);
            var segments = WhisperXTranscriptionProvider.ParseSegments(json);

            var words = segments
                .SelectMany(s => s.Words ?? [])
                .Select(w => new AlignedWord(w.Text, w.StartSeconds, w.EndSeconds))
                .ToList();

            return new AlignmentResult(words);
        }
        finally
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private static async Task RunWhisperXAsync(string executablePath, string mediaPath, string outputDir, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(mediaPath);
        startInfo.ArgumentList.Add("--output_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--output_dir");
        startInfo.ArgumentList.Add(outputDir);
        startInfo.ArgumentList.Add("--compute_type");
        startInfo.ArgumentList.Add("float32");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"whisperx exited with code {process.ExitCode}: {stderr}");
        }
    }
}
