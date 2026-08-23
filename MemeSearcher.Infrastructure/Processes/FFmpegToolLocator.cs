using MemeSearcher.Core.Settings;
using MemeSearcher.Infrastructure.Settings;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>Locates the system-installed ffmpeg executable - same rationale as EspeakToolLocator.</summary>
public class FFmpegToolLocator(ISettingsStore? settingsStore = null, ExternalToolSettings? toolSettings = null)
    : ExternalToolLocatorBase(settingsStore, toolSettings)
{
    public override string ToolName => "ffmpeg";

    protected override string ExecutableBaseName => "ffmpeg";

    protected override string VersionArgument => "-version";

    protected override string InstallHint => "Install FFmpeg: https://ffmpeg.org/download.html";

    // First line looks like "ffmpeg version 6.1.1 Copyright (c) ...".
    protected override string ParseVersion(string output) =>
        output.Split('\n').FirstOrDefault()?.Trim() ?? output.Trim();
}
