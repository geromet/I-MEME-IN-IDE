using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Locates the Montreal Forced Aligner CLI - same rationale as EspeakToolLocator, but this is the
/// tool most likely to need an explicit path: MFA's documented installation is a conda
/// environment, which is only on PATH while activated, and a GUI app never inherits that.
/// </summary>
public class MfaToolLocator(ISettingsStore? settingsStore = null, ExternalToolSettings? toolSettings = null)
    : ExternalToolLocatorBase(settingsStore, toolSettings)
{
    public override string ToolName => "mfa";

    protected override string ExecutableBaseName => "mfa";

    protected override string VersionArgument => "version";

    protected override string InstallHint => "Install Montreal Forced Aligner: https://montreal-forced-aligner.readthedocs.io/";
}
