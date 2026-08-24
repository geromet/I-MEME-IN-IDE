using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Locates the system-installed yt-dlp executable (#27) - same rationale/shape as the other four
/// locators. yt-dlp is unusually version-sensitive: it breaks against YouTube's own changes
/// constantly, and its own release cadence (a dated version string, e.g. "2026.08.19") is the only
/// signal the app has for "this is old enough that failures are more likely to be yt-dlp being
/// stale than a real problem." <see cref="IsVersionStale"/> turns that string into exactly that
/// yes/no - kept off ExternalToolStatus itself (which has no notion of "installed but you should
/// still worry") rather than widening a record every other locator also uses for one tool's need.
/// </summary>
public class YtDlpToolLocator(ISettingsStore? settingsStore = null, ExternalToolSettings? toolSettings = null)
    : ExternalToolLocatorBase(settingsStore, toolSettings)
{
    public override string ToolName => "yt-dlp";

    protected override string ExecutableBaseName => "yt-dlp";

    protected override string VersionArgument => "--version";

    protected override string InstallHint => "Install yt-dlp: https://github.com/yt-dlp/yt-dlp#installation";

    /// <summary>True once a "YYYY.MM.DD" version string is more than <paramref name="maxAgeDays"/> old relative to <paramref name="today"/> - both parameters exist purely so this is testable without depending on the real clock.</summary>
    public static bool IsVersionStale(string? version, DateOnly today, int maxAgeDays = 180)
    {
        if (version is null || !DateOnly.TryParseExact(version.Trim(), "yyyy.MM.dd", out var releaseDate))
        {
            return false;
        }

        return today.DayNumber - releaseDate.DayNumber > maxAgeDays;
    }
}
