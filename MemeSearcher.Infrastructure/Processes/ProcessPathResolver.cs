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
        PrependExecutableDirectoryToPath(startInfo, status.ExecutablePath);

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

    /// <summary>
    /// Puts the tool's own directory at the front of PATH for the spawned process.
    ///
    /// Running a tool by absolute path is not the same as running it from its environment. MFA
    /// shells out to sibling binaries - fstcompile and the rest of openfst - that live next to it
    /// in the conda env's bin, and finds them on PATH, not relative to itself. Launched by
    /// absolute path from an app that never activated the env, MFA starts fine and then dies with
    /// "Could not find 'fstcompile'. Please ensure that you have installed MFA's conda
    /// dependencies" - which reads as a broken install when the binaries are sitting right beside
    /// the executable that is looking for them.
    ///
    /// Prepended rather than appended so a tool's own siblings win over any same-named binary
    /// elsewhere on the system, which is what activating the environment would have done.
    /// </summary>
    private static void PrependExecutableDirectoryToPath(ProcessStartInfo startInfo, string? executablePath)
    {
        if (executablePath is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var existing = startInfo.Environment.TryGetValue("PATH", out var value) && !string.IsNullOrEmpty(value)
            ? value
            : Environment.GetEnvironmentVariable("PATH") ?? "";

        startInfo.Environment["PATH"] = existing.Length > 0
            ? directory + Path.PathSeparator + existing
            : directory;
    }
}
