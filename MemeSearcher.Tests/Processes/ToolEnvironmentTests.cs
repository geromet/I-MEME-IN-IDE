using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Processes;

public class ToolEnvironmentTests
{
    /// <summary>
    /// Running a tool by absolute path is not the same as running it from its environment. MFA
    /// finds its openfst siblings on PATH, so an absolute-path launch from an app that never
    /// activated the conda env fails with "Could not find 'fstcompile'" - with the binaries
    /// sitting right next to the executable that is looking for them.
    /// </summary>
    [Fact]
    public void ApplyToolEnvironment_PutsTheToolsOwnDirectoryFirstOnPath()
    {
        var status = new ExternalToolStatus(true, "/opt/env/bin/mfa", "3.4.2", null);

        var startInfo = new ProcessStartInfo("/opt/env/bin/mfa").ApplyToolEnvironment(status);

        Assert.StartsWith("/opt/env/bin" + Path.PathSeparator, startInfo.Environment["PATH"]);
    }

    [Fact]
    public void ApplyToolEnvironment_KeepsTheInheritedPathAfterTheToolsDirectory()
    {
        var status = new ExternalToolStatus(true, "/opt/env/bin/mfa", null, null);

        var startInfo = new ProcessStartInfo("/opt/env/bin/mfa").ApplyToolEnvironment(status);

        // Prepending, not replacing: the tool still needs the rest of the system.
        Assert.Contains(Environment.GetEnvironmentVariable("PATH")!, startInfo.Environment["PATH"]);
    }

    [Fact]
    public void ApplyToolEnvironment_AppliesConfiguredVariables()
    {
        var status = new ExternalToolStatus(
            true, "/opt/env/bin/mfa", null, null,
            new Dictionary<string, string> { ["PYTHONNOUSERSITE"] = "1" });

        var startInfo = new ProcessStartInfo("/opt/env/bin/mfa").ApplyToolEnvironment(status);

        Assert.Equal("1", startInfo.Environment["PYTHONNOUSERSITE"]);
        Assert.StartsWith("/opt/env/bin" + Path.PathSeparator, startInfo.Environment["PATH"]);
    }

    [Fact]
    public void ApplyToolEnvironment_ToleratesAToolThatWasNeverLocated()
    {
        var status = new ExternalToolStatus(false, null, null, "not found");

        var startInfo = new ProcessStartInfo("whatever").ApplyToolEnvironment(status);

        Assert.NotNull(startInfo);
    }

    /// <summary>
    /// The real thing, on this machine: MFA's openfst siblings must be reachable from a process
    /// spawned by absolute path with no conda activation. Skips when MFA is not installed here.
    /// </summary>
    [Fact]
    public void ApplyToolEnvironment_MakesCondaSiblingBinariesReachable()
    {
        var envs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".conda", "envs");

        var mfaPath = Directory.Exists(envs)
            ? Directory.GetDirectories(envs).Select(e => Path.Combine(e, "bin", "mfa")).FirstOrDefault(File.Exists)
            : null;

        if (mfaPath is null || !File.Exists(Path.Combine(Path.GetDirectoryName(mfaPath)!, "fstcompile")))
        {
            return;
        }

        var status = new ExternalToolStatus(true, mfaPath, null, null);
        var startInfo = new ProcessStartInfo(mfaPath).ApplyToolEnvironment(status);

        var reachable = startInfo.Environment["PATH"]!
            .Split(Path.PathSeparator)
            .Any(dir => dir.Length > 0 && File.Exists(Path.Combine(dir, "fstcompile")));

        Assert.True(reachable, "fstcompile must be reachable on the spawned process's PATH.");
    }
}
