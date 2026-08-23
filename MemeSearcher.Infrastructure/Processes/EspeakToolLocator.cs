using System.Diagnostics;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Locates the system-installed espeak-ng executable (handoff §35/§36: this app should not
/// assume a fixed path like /usr/bin/espeak-ng, and should be able to report a useful error
/// when it's missing rather than fail deep inside the phonemizer).
/// </summary>
public class EspeakToolLocator : IExternalToolLocator
{
    public string ToolName => "espeak-ng";

    public async Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default)
    {
        var executableName = OperatingSystem.IsWindows() ? "espeak-ng.exe" : "espeak-ng";
        var executablePath = ProcessPathResolver.FindOnPath(executableName);

        if (executablePath is null)
        {
            return new ExternalToolStatus(
                IsInstalled: false,
                ExecutablePath: null,
                Version: null,
                Error: $"'{executableName}' was not found on PATH. Install espeak-ng: https://github.com/espeak-ng/espeak-ng");
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
        var startInfo = new ProcessStartInfo(executablePath, "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output.Trim();
    }
}
