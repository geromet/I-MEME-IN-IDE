using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>Locates the system-installed ffprobe executable - same rationale as EspeakToolLocator.</summary>
public class FFprobeToolLocator(ISettingsStore? settingsStore = null, ExternalToolSettings? toolSettings = null)
    : ExternalToolLocatorBase(settingsStore, toolSettings)
{
    public override string ToolName => "ffprobe";

    protected override string ExecutableBaseName => "ffprobe";

    protected override string VersionArgument => "-version";

    protected override string InstallHint => "Install FFmpeg: https://ffmpeg.org/download.html";

    // First line looks like "ffprobe version 6.1.1 Copyright (c) ...".
    protected override string ParseVersion(string output) =>
        output.Split('\n').FirstOrDefault()?.Trim() ?? output.Trim();
}
