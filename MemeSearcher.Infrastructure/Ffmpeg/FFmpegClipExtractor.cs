using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Infrastructure.Ffmpeg;

public record ClipExtractionResult(bool Success, string? OutputPath, string? Error);

/// <summary>
/// Exports a search result (or a composite result's components - addendum §33) to a standalone
/// clip file via ffmpeg. Milestone 5's "clip extraction" item. Single-clip extraction uses
/// `-c copy` first (fast, no quality loss) and falls back to re-encoding only if that fails -
/// container/codec combinations that can't cut cleanly at an arbitrary non-keyframe timestamp
/// with a stream copy are common enough that this isn't an edge case. Composite extraction
/// extracts each component the same way, then stitches them with ffmpeg's concat demuxer -
/// verified against real ffmpeg output on this machine, not just documentation.
/// </summary>
public class FFmpegClipExtractor(FFmpegToolLocator toolLocator)
{
    public async Task<ClipExtractionResult> ExtractAsync(
        string mediaPath, double startSeconds, double endSeconds, string outputPath, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            return new ClipExtractionResult(false, null, $"ffmpeg is not available: {status.Error}");
        }

        if (!File.Exists(mediaPath))
        {
            return new ClipExtractionResult(false, null, $"Media file not found: {mediaPath}");
        }

        return await ExtractOneAsync(status, mediaPath, startSeconds, endSeconds, outputPath, cancellationToken);
    }

    public async Task<ClipExtractionResult> ExtractCompositeAsync(
        IReadOnlyList<(string MediaPath, double StartSeconds, double EndSeconds)> components,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            return new ClipExtractionResult(false, null, $"ffmpeg is not available: {status.Error}");
        }

        if (components.Count == 0)
        {
            return new ClipExtractionResult(false, null, "No components to assemble.");
        }

        var workDir = Directory.CreateTempSubdirectory("memesearcher-clip-").FullName;
        try
        {
            var partPaths = new List<string>();
            var extension = Path.GetExtension(outputPath) is { Length: > 0 } ext ? ext : Path.GetExtension(components[0].MediaPath);

            for (var i = 0; i < components.Count; i++)
            {
                var (mediaPath, start, end) = components[i];
                if (!File.Exists(mediaPath))
                {
                    return new ClipExtractionResult(false, null, $"Media file not found: {mediaPath}");
                }

                var partPath = Path.Combine(workDir, $"part{i}{extension}");
                var partResult = await ExtractOneAsync(status, mediaPath, start, end, partPath, cancellationToken);
                if (!partResult.Success)
                {
                    return new ClipExtractionResult(false, null, $"Failed to extract component {i + 1}: {partResult.Error}");
                }

                partPaths.Add(partPath);
            }

            var concatListPath = Path.Combine(workDir, "concat.txt");
            await File.WriteAllLinesAsync(
                concatListPath,
                partPaths.Select(p => $"file '{p.Replace("'", "'\\''")}'"),
                cancellationToken);

            return await RunFFmpegAsync(
                status,
                ["-y", "-f", "concat", "-safe", "0", "-i", concatListPath, "-c", "copy", outputPath],
                outputPath,
                cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private static async Task<ClipExtractionResult> ExtractOneAsync(
        ExternalToolStatus status, string mediaPath, double startSeconds, double endSeconds, string outputPath, CancellationToken cancellationToken)
    {
        var start = startSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var end = endSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var copyResult = await RunFFmpegAsync(
            status, ["-y", "-i", mediaPath, "-ss", start, "-to", end, "-c", "copy", outputPath], outputPath, cancellationToken);
        if (copyResult.Success)
        {
            return copyResult;
        }

        // Stream copy can fail when the cut points aren't on keyframe boundaries for this
        // codec/container - re-encoding is slower but always works.
        return await RunFFmpegAsync(
            status, ["-y", "-i", mediaPath, "-ss", start, "-to", end, outputPath], outputPath, cancellationToken);
    }

    private static async Task<ClipExtractionResult> RunFFmpegAsync(
        ExternalToolStatus status, IEnumerable<string> arguments, string outputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(status.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
            ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

        // Both streams must be drained concurrently with waiting for exit, not just stderr - a
        // redirected pipe nobody reads fills its OS buffer, and the process then blocks forever
        // on write() while this method blocks forever on WaitForExitAsync (#33's follow-up: found
        // this exact deadlock in MfaAlignmentProvider's identical pattern, live, on a real run).
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cancellationToken);
        await stdoutTask; // discarded - only draining the pipe matters, not its content.

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            var stderr = await stderrTask;
            return new ClipExtractionResult(false, null, $"ffmpeg exited with code {process.ExitCode}: {stderr}");
        }

        return new ClipExtractionResult(true, outputPath, null);
    }
}
