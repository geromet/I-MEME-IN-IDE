using System.Diagnostics;
using System.Globalization;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Opens media in an external player rather than embedding one (handoff §22: in-app playback is
/// explicitly not a milestone-1 requirement). Tries known players that accept a start-time flag
/// first, in order, so the result actually opens at the matched timestamp; falls back to the OS's
/// default file handler - which opens the file but can't be told where to start - only if none
/// of them are installed.
/// </summary>
public class ExternalMediaPlayerLauncher : IMediaPlayerLauncher
{
    private static readonly (string Executable, Func<double, string> SeekArgument)[] KnownPlayers =
    [
        ("mpv", seconds => $"--start={seconds.ToString(CultureInfo.InvariantCulture)}"),
        ("vlc", seconds => $"--start-time={seconds.ToString(CultureInfo.InvariantCulture)}"),
    ];

    public Task<MediaLaunchResult> OpenAsync(string mediaPath, double startSeconds, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mediaPath))
        {
            return Task.FromResult(new MediaLaunchResult(false, false, $"Media file not found: {mediaPath}"));
        }

        foreach (var (executable, seekArgument) in KnownPlayers)
        {
            var executableName = OperatingSystem.IsWindows() ? $"{executable}.exe" : executable;
            var executablePath = ProcessPathResolver.FindOnPath(executableName);
            if (executablePath is null)
            {
                continue;
            }

            try
            {
                var startInfo = new ProcessStartInfo(executablePath) { UseShellExecute = false };
                startInfo.ArgumentList.Add(seekArgument(startSeconds));
                startInfo.ArgumentList.Add(mediaPath);
                Process.Start(startInfo);
                return Task.FromResult(new MediaLaunchResult(true, true, null));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new MediaLaunchResult(false, false, ex.Message));
            }
        }

        try
        {
            OpenWithDefaultApplication(mediaPath);
            return Task.FromResult(new MediaLaunchResult(true, false, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new MediaLaunchResult(false, false, ex.Message));
        }
    }

    private static void OpenWithDefaultApplication(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", path);
        }
        else
        {
            Process.Start("xdg-open", path);
        }
    }
}
