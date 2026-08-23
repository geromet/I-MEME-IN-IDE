using System.Diagnostics;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Processes;

public static class ProcessPathResolver
{
    /// <summary>
    /// An explicitly configured path wins over PATH, and is returned even when it does not exist so
    /// the caller can report "the path you configured is wrong" rather than silently falling back
    /// to a different executable than the one the user asked for.
    /// </summary>
    public static string? Resolve(string? configuredPath, string executableName) =>
        configuredPath ?? FindOnPath(executableName);

    public static string? FindOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public static class ProcessStartInfoExtensions
{
    /// <summary>
    /// Applies a tool's configured environment overrides to a process about to be started.
    ///
    /// Must be called at every spawn site for a tool, including the locator's own version probe -
    /// a tool that needs PYTHONNOUSERSITE to import its dependencies needs it just as much to
    /// print its version, and a locator that reports "found it but could not run it" while the
    /// real invocation works (or vice versa) is worse than either failure alone.
    /// </summary>
    public static ProcessStartInfo ApplyToolEnvironment(this ProcessStartInfo startInfo, ExternalToolStatus status)
    {
        if (status.Environment is null)
        {
            return startInfo;
        }

        foreach (var (key, value) in status.Environment)
        {
            startInfo.Environment[key] = value;
        }

        return startInfo;
    }
}
