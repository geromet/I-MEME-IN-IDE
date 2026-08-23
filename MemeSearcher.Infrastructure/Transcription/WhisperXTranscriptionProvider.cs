using System.Diagnostics;
using System.Text.Json;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Languages;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Transcription;

/// <summary>
/// Wraps the `whisperx` CLI directly, the same pattern as EspeakPhonemizer: no custom worker
/// script, shell out to the installed tool and parse its output. WhisperX decodes audio/video
/// itself (via ffmpeg internally), so this needs no separate audio-extraction step - handoff §25
/// keeps ITranscriptionProvider and IAlignmentProvider conceptually distinct, but WhisperX's own
/// CLI happens to do both in one pass; this class exposes both halves of that one invocation
/// (segment-level text/timing, and - Milestone 5 - the per-word alignment WhisperX already
/// computed as part of producing the transcript) rather than throwing the word data away and
/// making a caller re-run whisperx separately just to get it. A genuinely separate
/// WhisperXAlignmentProvider still exists for the case where the transcript came from somewhere
/// else entirely (e.g. an imported SRT) and needs aligning against audio on its own.
/// </summary>
public class WhisperXTranscriptionProvider(
    WhisperXToolLocator toolLocator,
    ISettingsStore settings,
    WhisperXSettings whisperXSettings) : ITranscriptionProvider
{
    public string ProviderName => "whisperx";

    public async Task<TranscriptionResult> TranscribeAsync(string mediaPath, string? language, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            throw new InvalidOperationException($"whisperx is not available: {status.Error}");
        }

        // whisperx takes ISO 639-1 codes and rejects region-qualified tags like "en-US" via
        // argparse `choices` - the run dies before any audio is read (#23). Resolve the neutral id
        // to whisperx's own code here, and let an unknown id fail with a readable message rather
        // than a wall of argparse output. A null language is left null: that is whisperx's
        // documented "detect the language" mode, not a missing value.
        var whisperCode = language is null ? null : LanguageCatalog.Get(language).WhisperCode;

        // Settings, not hardcoded values (#24). Validate the combination before spawning: a bad
        // device/compute pairing otherwise surfaces as a Python traceback minutes into a run.
        if (whisperXSettings.Validate(settings) is { } settingsError)
        {
            throw new InvalidOperationException($"WhisperX settings are not usable: {settingsError}");
        }

        var provenance = new TranscriptionProvenance(
            Model: settings.Get(WhisperXSettings.Model),
            Device: whisperXSettings.ResolveDevice(settings),
            ComputeType: settings.Get(WhisperXSettings.ComputeType));

        var outputDir = Directory.CreateTempSubdirectory("memesearcher-whisperx-").FullName;
        try
        {
            await RunWhisperXAsync(status, mediaPath, whisperCode, provenance, outputDir, cancellationToken);

            var outputJsonPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(mediaPath) + ".json");
            if (!File.Exists(outputJsonPath))
            {
                throw new InvalidOperationException(
                    $"whisperx did not produce the expected output file '{outputJsonPath}'.");
            }

            var json = await File.ReadAllTextAsync(outputJsonPath, cancellationToken);
            var segments = ParseSegments(json);

            // Report back the neutral id, not whisperx's code - this value is stored on
            // Transcript.Language and has to be resolvable through LanguageCatalog later.
            return new TranscriptionResult(language ?? "unknown", segments, provenance);
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
    /// "start"/"end"/"text" and (Milestone 5) a "words" array of {"word","start","end","score"}.
    /// A word can be present without "start"/"end" when WhisperX's alignment step failed for that
    /// specific word (a known real-world gotcha, not hypothetical) - those are skipped rather than
    /// guessed at, since MediaIngestionService's interpolation fallback already handles "no real
    /// timing for this word" gracefully when the word count doesn't line up.
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

            var words = segment.TryGetProperty("words", out var wordsProp) ? ParseWords(wordsProp) : null;

            result.Add(new TranscribedSegment(startProp.GetDouble(), endProp.GetDouble(), text, words));
        }

        return result;
    }

    private static IReadOnlyList<TranscribedWord>? ParseWords(JsonElement wordsProp)
    {
        var words = new List<TranscribedWord>();

        foreach (var word in wordsProp.EnumerateArray())
        {
            if (!word.TryGetProperty("word", out var wordTextProp) ||
                !word.TryGetProperty("start", out var startProp) ||
                !word.TryGetProperty("end", out var endProp))
            {
                continue; // Alignment failed for this word - no timing to use.
            }

            var text = wordTextProp.GetString()?.Trim() ?? "";
            if (text.Length == 0)
            {
                continue;
            }

            words.Add(new TranscribedWord(text, startProp.GetDouble(), endProp.GetDouble()));
        }

        return words.Count > 0 ? words : null;
    }

    private static async Task RunWhisperXAsync(
        ExternalToolStatus status,
        string mediaPath,
        string? whisperCode,
        TranscriptionProvenance provenance,
        string outputDir,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(status.ExecutablePath!)
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
        startInfo.ArgumentList.Add(provenance.ComputeType);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(provenance.Model);
        // Always pass --device explicitly. whisperx's own default is `cuda`, so omitting this - as
        // this class previously did - silently selects a GPU on machines that have none.
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(provenance.Device);

        if (!string.IsNullOrWhiteSpace(whisperCode))
        {
            startInfo.ArgumentList.Add("--language");
            startInfo.ArgumentList.Add(whisperCode);
        }

        using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
            ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"whisperx exited with code {process.ExitCode}: {stderr}");
        }
    }
}
