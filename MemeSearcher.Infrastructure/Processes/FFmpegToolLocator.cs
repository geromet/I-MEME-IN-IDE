using System.Diagnostics;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>Locates the system-installed ffmpeg executable - same rationale as EspeakToolLocator.</summary>
public class FFmpegToolLocator : IExternalToolLocator
{
    public string ToolName => "ffmpeg";

    public async Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default)
    {
        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var executablePath = ProcessPathResolver.FindOnPath(executableName);

        if (executablePath is null)
        {
            return new ExternalToolStatus(
                IsInstalled: false,
                ExecutablePath: null,
                Version: null,
                Error: $"'{executableName}' was not found on PATH. Install FFmpeg: https://ffmpeg.org/download.html");
        }

        try
        {
            var version = await GetVersionAsync(executablePath, cancellationToken);
            return new ExternalToolStatus(true, executablePath, version, null);
        }
        catch (Exception ex)
        {
            return new ExternalToolStatus(false, executablePath, null, $"Found '{executableName}' but failed to run it: {ex.Message}");
        }
    }

    private static async Task<string> GetVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath, "-version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output.Split('\n').FirstOrDefault()?.Trim() ?? output.Trim();
    }
}
