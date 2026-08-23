using System.Diagnostics;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>Locates the system-installed Montreal Forced Aligner CLI - same rationale as EspeakToolLocator.</summary>
public class MfaToolLocator : IExternalToolLocator
{
    public string ToolName => "mfa";

    public async Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default)
    {
        var executableName = OperatingSystem.IsWindows() ? "mfa.exe" : "mfa";
        var executablePath = ProcessPathResolver.FindOnPath(executableName);

        if (executablePath is null)
        {
            return new ExternalToolStatus(
                IsInstalled: false,
                ExecutablePath: null,
                Version: null,
                Error: $"'{executableName}' was not found on PATH. Install Montreal Forced Aligner: https://montreal-forced-aligner.readthedocs.io/");
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
        var startInfo = new ProcessStartInfo(executablePath, "version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executablePath}'.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output.Trim();
    }
}
