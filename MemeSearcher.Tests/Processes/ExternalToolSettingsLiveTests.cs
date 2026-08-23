using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Tests.TestDoubles;

namespace MemeSearcher.Tests.Processes;

/// <summary>
/// Exercises the configured-path and environment-override plumbing against a real conda-installed
/// MFA when one is present, and skips otherwise. This is the case the whole category exists for:
/// the tool is installed and working in a shell, and invisible to a GUI app.
/// </summary>
public class ExternalToolSettingsLiveTests
{
    private static string? FindCondaMfa()
    {
        var candidates = Directory.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".conda", "envs"))
            ? Directory.GetDirectories(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".conda", "envs"))
            : [];

        return candidates
            .Select(env => Path.Combine(env, "bin", "mfa"))
            .FirstOrDefault(File.Exists);
    }

    [Fact]
    public async Task ConfiguredPath_MakesAToolFoundThatPathSearchMisses()
    {
        var mfaPath = FindCondaMfa();
        if (mfaPath is null || ProcessPathResolver.FindOnPath("mfa") is not null)
        {
            return; // No conda MFA, or it's already on PATH so there's nothing to prove.
        }

        var store = new InMemorySettingsStore();
        var toolSettings = new ExternalToolSettings();

        Assert.False((await new MfaToolLocator().LocateAsync()).IsInstalled);

        store.Set(ExternalToolSettings.PathSetting("mfa"), mfaPath);
        store.Set(ExternalToolSettings.EnvironmentSetting("mfa"), "PYTHONNOUSERSITE=1");

        var status = await new MfaToolLocator(store, toolSettings).LocateAsync();

        Assert.True(status.IsInstalled, $"Expected MFA to be located and runnable. Error: {status.Error}");
        Assert.Equal(mfaPath, status.ExecutablePath);
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
    }

    /// <summary>
    /// Without the environment override the same executable is found but cannot run - a conda env
    /// sharing a Python version with the user's system Python imports ~/.local packages ahead of
    /// its own. The two halves of this feature are separately necessary.
    /// </summary>
    [Fact]
    public async Task ConfiguredPathAlone_IsNotEnoughWhenUserSitePackagesShadowTheEnvironment()
    {
        var mfaPath = FindCondaMfa();
        if (mfaPath is null)
        {
            return;
        }

        var store = new InMemorySettingsStore();
        store.Set(ExternalToolSettings.PathSetting("mfa"), mfaPath);

        var withoutOverride = await new MfaToolLocator(store, new ExternalToolSettings()).LocateAsync();

        store.Set(ExternalToolSettings.EnvironmentSetting("mfa"), "PYTHONNOUSERSITE=1");
        var withOverride = await new MfaToolLocator(store, new ExternalToolSettings()).LocateAsync();

        // The override must never make things worse; on a machine with no shadowing both succeed.
        Assert.True(withOverride.IsInstalled, $"Expected MFA to run with the override. Error: {withOverride.Error}");

        if (!withoutOverride.IsInstalled)
        {
            // This machine reproduces the shadowing - the error should name the real cause rather
            // than just saying the tool failed.
            Assert.Contains("numpy", withoutOverride.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ConfiguredPathThatDoesNotExist_ReportsTheSettingRatherThanFallingBackToPath()
    {
        var store = new InMemorySettingsStore();
        store.Set(ExternalToolSettings.PathSetting("espeak-ng"), "/nonexistent/espeak-ng");

        var status = await new EspeakToolLocator(store, new ExternalToolSettings()).LocateAsync();

        Assert.False(status.IsInstalled);
        Assert.Contains("configured path", status.Error!);
    }
}
