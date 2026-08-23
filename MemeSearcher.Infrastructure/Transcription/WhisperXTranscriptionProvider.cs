using System.Diagnostics;
using System.Text.Json;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Infrastructure.Transcription;

/// <summary>
/// Wraps the `whisperx` CLI directly, the same pattern as EspeakPhonemizer: no custom worker
/// script, shell out to the installed tool and parse its output. WhisperX decodes audio/video
/// itself (via ffmpeg internally), so this needs no separate audio-extraction step - handoff §25
/// keeps ITranscriptionProvider and IAlignmentProvider conceptually distinct, but WhisperX's own
/// CLI happens to do both; this class only claims the transcription half (segment-level text +
/// timing). Word-level timestamps that whisperx also produces aren't consumed here - the existing
/// phonemizer-driven word split/interpolation (handoff §49/50: predicted vs actual pronunciation)
/// already covers "no real per-word alignment yet"; consuming whisperx's word timing is a natural
/// follow-up, not required to get transcription working.
/// </summary>
public class WhisperXTranscriptionProvider(WhisperXToolLocator toolLocator) : ITranscriptionProvider
{
    public string ProviderName => "whisperx";

    public async Task<TranscriptionResult> TranscribeAsync(string mediaPath, string? language, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"whisperx is not available: {status.Error}");
        }

        var outputDir = Directory.CreateTempSubdirectory("memesearcher-whisperx-").FullName;
        try
        {
            await RunWhisperXAsync(status.ExecutablePath!, mediaPath, language, outputDir, cancellationToken);

            var outputJsonPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(mediaPath) + ".json");
            if (!File.Exists(outputJsonPath))
            {
                throw new InvalidOperationException(
                    $"whisperx did not produce the expected output file '{outputJsonPath}'.");
            }

            var json = await File.ReadAllTextAsync(outputJsonPath, cancellationToken);
            var segments = ParseSegments(json);

            return new TranscriptionResult(language ?? "unknown", segments);
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

    /// <summary>
    /// Parses whisperx's `--output_format json` output: a top-level "segments" array, each with
    /// "start"/"end"/"text" (documented WhisperX output shape). Deliberately ignores the
    /// per-segment "words" array - see class remarks.
    /// </summary>
    public static IReadOnlyList<TranscribedSegment> ParseSegments(string whisperXJson)
    {
        using var document = JsonDocument.Parse(whisperXJson);

        if (!document.RootElement.TryGetProperty("segments", out var segments))
        {
            return [];
        }

        var result = new List<TranscribedSegment>();
        foreach (var segment in segments.EnumerateArray())
        {
            if (!segment.TryGetProperty("start", out var startProp) ||
                !segment.TryGetProperty("end", out var endProp) ||
                !segment.TryGetProperty("text", out var textProp))
            {
                continue;
            }

            var text = textProp.GetString()?.Trim() ?? "";
            if (text.Length == 0)
            {
                continue;
            }

            result.Add(new TranscribedSegment(startProp.GetDouble(), endProp.GetDouble(), text));
        }

        return result;
    }

    private static async Task RunWhisperXAsync(
        string executablePath, string mediaPath, string? language, string outputDir, CancellationToken cancellationToken)
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
        // float16 is the whisperx default and fails outright on CPU-only machines; float32 works
        // everywhere, at a performance cost on GPUs that support float16. Not user-configurable
        // yet - a reasonable default until there's a settings surface for it.
        startInfo.ArgumentList.Add("--compute_type");
        startInfo.ArgumentList.Add("float32");

        if (!string.IsNullOrWhiteSpace(language))
        {
            startInfo.ArgumentList.Add("--language");
            startInfo.ArgumentList.Add(language);
        }

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
