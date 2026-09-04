using System.Diagnostics;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Processes;

/// <summary>
/// Exercises the real process-launch boundary where practical and verifies command construction
/// directly where launching a desktop handler would be environment-specific.
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

    [Theory]
    [InlineData(false, true, "open")]
    [InlineData(false, false, "xdg-open")]
    public void BuildDefaultApplicationStartInfo_NonWindowsPreservesSpacedPathAsOneArgument(
        bool isWindows,
        bool isMacOS,
        string expectedExecutable)
    {
        const string path = "/tmp/meme corpus/clip one.mp4";

        var startInfo = ExternalMediaPlayerLauncher.BuildDefaultApplicationStartInfo(path, isWindows, isMacOS);

        Assert.Equal(expectedExecutable, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Single(startInfo.ArgumentList);
        Assert.Equal(path, startInfo.ArgumentList[0]);
        Assert.Empty(startInfo.Arguments);
    }

    [Fact]
    public void BuildDefaultApplicationStartInfo_WindowsKeepsRegisteredHandlerBehavior()
    {
        const string path = @"C:\meme corpus\clip one.mp4";

        var startInfo = ExternalMediaPlayerLauncher.BuildDefaultApplicationStartInfo(path, isWindows: true, isMacOS: false);

        Assert.Equal(path, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
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
