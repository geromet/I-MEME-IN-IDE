using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>Locates the system-installed whisperx CLI - same rationale as EspeakToolLocator.</summary>
public class WhisperXToolLocator(ISettingsStore? settingsStore = null, ExternalToolSettings? toolSettings = null)
    : ExternalToolLocatorBase(settingsStore, toolSettings)
{
    public override string ToolName => "whisperx";

    protected override string ExecutableBaseName => "whisperx";

    protected override string VersionArgument => "--version";

    protected override string InstallHint => "Install WhisperX: https://github.com/m-bain/whisperX";
}
