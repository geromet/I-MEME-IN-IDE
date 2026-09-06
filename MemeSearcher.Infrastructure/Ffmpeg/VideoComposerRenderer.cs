using System.Diagnostics;
using System.Globalization;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Infrastructure.Ffmpeg;

public sealed record VideoRenderResult(bool Success, string? OutputPath, string? Error);

public enum VideoRenderStage
{
    Preparing,
    Rendering,
    Completed,
}

public sealed record VideoRenderProgress(
    VideoRenderStage Stage,
    TimeSpan Processed,
    double? Fraction);

/// <summary>
/// Executes a validated <see cref="VideoRenderPlan"/> with the application's existing ffmpeg
/// locator and shared process-cancellation semantics. The renderer owns only the output-file
/// effect: failed or cancelled renders are removed so callers never treat a partial file as a
/// successful export.
/// </summary>
public sealed class VideoComposerRenderer([FromKeyedServices("ffmpeg")] IExternalToolLocator toolLocator)
{
    private const int MaxErrorLength = 4_000;

    public Task<VideoRenderResult> RenderAsync(
        VideoRenderPlan plan,
        CancellationToken cancellationToken = default) =>
        RenderAsync(plan, progress: null, cancellationToken);

    public async Task<VideoRenderResult> RenderAsync(
        VideoRenderPlan plan,
        IProgress<VideoRenderProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var expectedDuration = GetExpectedDuration(plan);
        progress?.Report(new VideoRenderProgress(
            VideoRenderStage.Preparing,
            TimeSpan.Zero,
            expectedDuration > TimeSpan.Zero ? 0d : null));

        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled || string.IsNullOrWhiteSpace(status.ExecutablePath))
        {
            return new VideoRenderResult(false, null, $"ffmpeg is not available: {status.Error}");
        }

        var outputDirectory = Path.GetDirectoryName(plan.OutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return new VideoRenderResult(false, null, $"Render output directory does not exist: {outputDirectory}");
        }

        DeletePartialOutput(plan.OutputPath);

        var startInfo = new ProcessStartInfo(status.ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Use ffmpeg's machine-readable progress stream rather than scraping human stderr.
        // The planner remains the sole owner of media/render arguments; these are execution-only
        // diagnostics and do not alter output semantics.
        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add("-nostats");
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
                ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

            // stderr remains the actionable diagnostics channel while stdout carries structured
            // progress key/value records. Drain both concurrently to avoid pipe backpressure.
            var progressTask = ReadProgressAsync(process.StandardOutput, expectedDuration, progress);
            var stderrTask = process.StandardError.ReadToEndAsync();

            await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cancellationToken);
            await progressTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(plan.OutputPath))
            {
                DeletePartialOutput(plan.OutputPath);
                var detail = process.ExitCode == 0
                    ? "ffmpeg exited successfully but did not produce the requested output file."
                    : $"ffmpeg exited with code {process.ExitCode}: {TrimError(stderr)}";
                return new VideoRenderResult(false, null, detail);
            }

            progress?.Report(new VideoRenderProgress(
                VideoRenderStage.Completed,
                expectedDuration,
                expectedDuration > TimeSpan.Zero ? 1d : null));
            return new VideoRenderResult(true, plan.OutputPath, null);
        }
        catch (OperationCanceledException)
        {
            DeletePartialOutput(plan.OutputPath);
            throw;
        }
        catch (Exception ex)
        {
            DeletePartialOutput(plan.OutputPath);
            return new VideoRenderResult(false, null, $"Failed to render video: {ex.Message}");
        }
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        TimeSpan expectedDuration,
        IProgress<VideoRenderProgress>? progress)
    {
        if (progress is null)
        {
            await reader.ReadToEndAsync();
            return;
        }

        while (await reader.ReadLineAsync() is { } line)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            if (!string.Equals(key, "out_time_us", StringComparison.Ordinal)
                || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds)
                || microseconds < 0)
            {
                continue;
            }

            var processed = TimeSpan.FromMicroseconds(microseconds);
            double? fraction = null;
            if (expectedDuration > TimeSpan.Zero)
            {
                fraction = Math.Clamp(processed.TotalSeconds / expectedDuration.TotalSeconds, 0d, 1d);
            }

            progress.Report(new VideoRenderProgress(VideoRenderStage.Rendering, processed, fraction));
        }
    }

    private static TimeSpan GetExpectedDuration(VideoRenderPlan plan)
    {
        var seconds = plan.Inputs.Sum(input => Math.Max(0d, input.EndSeconds - input.StartSeconds));
        return seconds > 0d && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    private static string TrimError(string stderr)
    {
        var trimmed = stderr.Trim();
        return trimmed.Length <= MaxErrorLength ? trimmed : trimmed[^MaxErrorLength..];
    }

    private static void DeletePartialOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup. A locked partial output is still never reported as success.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup. The returned/cancelled state remains authoritative.
        }
    }
}
