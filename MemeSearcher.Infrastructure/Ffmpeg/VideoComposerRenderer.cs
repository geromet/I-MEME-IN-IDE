using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Infrastructure.Ffmpeg;

public sealed record VideoRenderResult(bool Success, string? OutputPath, string? Error);

/// <summary>
/// Executes a validated <see cref="VideoRenderPlan"/> with the application's existing ffmpeg
/// locator and shared process-cancellation semantics. The renderer owns only the output-file
/// effect: failed or cancelled renders are removed so callers never treat a partial file as a
/// successful export.
/// </summary>
public sealed class VideoComposerRenderer([FromKeyedServices("ffmpeg")] IExternalToolLocator toolLocator)
{
    private const int MaxErrorLength = 4_000;

    public async Task<VideoRenderResult> RenderAsync(
        VideoRenderPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

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
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
                ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

            // ffmpeg writes progress/diagnostics to stderr. Drain both redirected streams while the
            // process runs so neither OS pipe can fill and deadlock a long render.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cancellationToken);
            await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(plan.OutputPath))
            {
                DeletePartialOutput(plan.OutputPath);
                var detail = process.ExitCode == 0
                    ? "ffmpeg exited successfully but did not produce the requested output file."
                    : $"ffmpeg exited with code {process.ExitCode}: {TrimError(stderr)}";
                return new VideoRenderResult(false, null, detail);
            }

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
