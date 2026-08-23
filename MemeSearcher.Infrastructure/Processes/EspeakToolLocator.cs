using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Locates the system-installed espeak-ng executable (handoff §35/§36: this app should not
/// assume a fixed path like /usr/bin/espeak-ng, and should be able to report a useful error
/// when it's missing rather than fail deep inside the phonemizer).
/// </summary>
public class EspeakToolLocator(ISettingsStore? settingsStore = null, ExternalToolSettings? toolSettings = null)
    : ExternalToolLocatorBase(settingsStore, toolSettings)
{
    public override string ToolName => "espeak-ng";

    protected override string ExecutableBaseName => "espeak-ng";

    protected override string VersionArgument => "--version";

    protected override string InstallHint => "Install espeak-ng: https://github.com/espeak-ng/espeak-ng";
}
