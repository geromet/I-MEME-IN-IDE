using System.Diagnostics;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Processes;

/// <summary>
/// Exercises the real process-launch boundary (mpv, confirmed installed on this machine) rather
/// than mocking it - the risk here is entirely "does the CLI invocation actually work," which a
/// mock can't catch. Doesn't assert anything about actual playback (mpv is given a non-media file
/// so it exits quickly on its own) - only that OpenAsync correctly finds a seek-capable player and
/// launches it without throwing.
/// </summary>
public class ExternalMediaPlayerLauncherTests
{
    [Fact]
    public async Task OpenAsync_MissingFileReturnsFailureWithoutSpawningAnything()
    {
        var launcher = new ExternalMediaPlayerLauncher();

        var result = await launcher.OpenAsync("/no/such/file.mp4", 12.5);

        Assert.False(result.Success);
        Assert.False(result.SeekedToTimestamp);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task OpenAsync_WithAnExistingFileAndASeekCapablePlayerReportsSeeked()
    {
        if (!await IsAvailableAsync("mpv") && !await IsAvailableAsync("vlc"))
        {
            return; // Neither known seek-capable player is installed on this machine.
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"memesearcher-playertest-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(tempFile, "not actually a video, just needs to exist");

        try
        {
            var launcher = new ExternalMediaPlayerLauncher();
            var result = await launcher.OpenAsync(tempFile, 3.0);

            Assert.True(result.Success);
            Assert.True(result.SeekedToTimestamp);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static async Task<bool> IsAvailableAsync(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
