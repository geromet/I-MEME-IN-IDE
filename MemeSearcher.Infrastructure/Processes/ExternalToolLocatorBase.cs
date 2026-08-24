using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Shared behaviour for locating an external command-line tool: honour an explicitly configured
/// path, otherwise search PATH, then prove the tool actually runs by asking for its version.
///
/// The five locators were near-identical copies before this. Collapsing them matters here beyond
/// tidiness: path configuration and environment overrides have to apply to *every* tool
/// consistently, and five hand-maintained copies is how one of them quietly ends up not
/// supporting the setting.
///
/// Settings are optional so a locator can still be constructed standalone (tests do this, and the
/// behaviour is then exactly what it was before settings existed: search PATH, no overrides).
/// </summary>
public abstract class ExternalToolLocatorBase(
    ISettingsStore? settingsStore = null,
    ExternalToolSettings? toolSettings = null) : IExternalToolLocator
{
    public abstract string ToolName { get; }

    /// <summary>Executable name without any platform extension.</summary>
    protected abstract string ExecutableBaseName { get; }

    /// <summary>Argument that makes the tool print its version, e.g. "--version" or "-version".</summary>
    protected abstract string VersionArgument { get; }

    /// <summary>Appended to the not-found error, e.g. "Install FFmpeg: https://...".</summary>
    protected abstract string InstallHint { get; }

    /// <summary>Some tools print a banner; those override this to keep only the first line.</summary>
    protected virtual string ParseVersion(string output) => output.Trim();

    public async Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default)
    {
        var executableName = OperatingSystem.IsWindows() ? ExecutableBaseName + ".exe" : ExecutableBaseName;

        var configuredPath = settingsStore is not null && toolSettings is not null
            ? toolSettings.GetConfiguredPath(settingsStore, ToolName)
            : null;

        var environment = settingsStore is not null && toolSettings is not null
            ? toolSettings.GetEnvironment(settingsStore, ToolName)
            : null;

        var executablePath = ProcessPathResolver.Resolve(configuredPath, executableName);

        if (executablePath is null)
        {
            return new ExternalToolStatus(
                IsInstalled: false,
                ExecutablePath: null,
                Version: null,
                Error: $"'{executableName}' was not found on PATH. {InstallHint} If it is installed "
                       + $"somewhere PATH does not reach - a conda environment, for example - set its "
                       + $"path in Settings under \"{ExternalToolSettings.CategoryName}\".");
        }

        // A configured path that does not exist is reported as such rather than falling back to
        // PATH: silently running a different executable than the one that was asked for is worse
        // than saying the setting is wrong.
        if (configuredPath is not null && !File.Exists(configuredPath))
        {
            return new ExternalToolStatus(
                IsInstalled: false,
                ExecutablePath: configuredPath,
                Version: null,
                Error: $"The configured path for {ToolName} does not exist: '{configuredPath}'.",
                Environment: environment);
        }

        var status = new ExternalToolStatus(true, executablePath, null, null, environment);

        try
        {
            var version = await GetVersionAsync(status, cancellationToken);
            return status with { Version = version };
        }
        catch (Exception ex)
        {
            return new ExternalToolStatus(
                false, executablePath, null,
                $"Found '{executablePath}' but failed to run it: {ex.Message}", environment);
        }
    }

    private async Task<string> GetVersionAsync(ExternalToolStatus status, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(status.ExecutablePath!, VersionArgument)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }.ApplyToolEnvironment(status);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cancellationToken);

        // A tool that is present but cannot import its own dependencies exits non-zero and says why
        // on stderr. Surfacing that is the difference between "MFA is broken somehow" and a message
        // naming the actual import error - which is the case that prompted this whole category.
        if (process.ExitCode != 0)
        {
            var detail = error.Trim().Length > 0 ? error.Trim() : output.Trim();
            var lastLine = detail.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "no output";

            throw new InvalidOperationException($"exit code {process.ExitCode}: {lastLine}");
        }

        return ParseVersion(output.Trim().Length > 0 ? output : error);
    }
}
