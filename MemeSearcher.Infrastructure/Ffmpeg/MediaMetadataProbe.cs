using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Infrastructure.Ffmpeg;

/// <summary>
/// Reads basic media metadata (currently just duration) via ffprobe. This is the "optional
/// FFmpeg integration" from the addendum's Milestone 3 - kept separate from transcription, since
/// WhisperX decodes audio/video itself and doesn't need FFmpeg pre-extraction to run.
/// </summary>
public class MediaMetadataProbe(FFprobeToolLocator toolLocator)
{
    public async Task<TimeSpan?> TryGetDurationAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(status.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(mediaPath);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return null;
        }

        return ParseDuration(stdout);
    }

    /// <summary>
    /// Parses ffprobe's `-show_entries format=duration -of json` output, e.g.
    /// `{"format": {"duration": "2.000000"}}` (verified against a real ffprobe invocation).
    /// </summary>
    public static TimeSpan? ParseDuration(string ffprobeJson)
    {
        try
        {
            using var document = JsonDocument.Parse(ffprobeJson);
            if (!document.RootElement.TryGetProperty("format", out var format)
                || !format.TryGetProperty("duration", out var durationProperty))
            {
                return null;
            }

            var durationText = durationProperty.GetString();
            if (durationText is null || !double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return null;
            }

            return TimeSpan.FromSeconds(seconds);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
