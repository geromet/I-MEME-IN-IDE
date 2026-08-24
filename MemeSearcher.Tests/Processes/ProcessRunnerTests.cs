using System.Diagnostics;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Processes;

/// <summary>
/// #14's hard exit criterion: "an in-flight ffmpeg operation can be cancelled and the child
/// process provably exits." Provably means checking the OS process, not the .NET Process object -
/// asserting on `process.HasExited` proves nothing, since that object is disposed/abandoned either
/// way once WaitForExitAsync stops awaiting it. This starts a real, genuinely long-running ffmpeg
/// encode (not `sleep`, since the criterion names ffmpeg specifically), captures its OS PID, and
/// after cancelling checks that PID is actually gone from the process table.
/// </summary>
public class ProcessRunnerTests
{
    [Fact]
    public async Task WaitForExitAndKillOnCancelAsync_CancellingAnInFlightFfmpegRun_ActuallyKillsTheOsProcess()
    {
        var locator = new FFmpegToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo(status.ExecutablePath!)
        {
            // A synthetic 300-second encode with no input file needed - long enough that it is
            // still running when we cancel a moment later, short enough not to leave a real
            // orphan behind if this test's own kill logic were to fail.
            ArgumentList = { "-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30", "-t", "300", "-f", "null", "-" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        var pid = process.Id;
        Assert.True(IsProcessAlive(pid), "ffmpeg should have started and still be running.");

        using var cts = new CancellationTokenSource();
        var waitTask = ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cts.Token);

        await Task.Delay(300);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);

        // Killing is not instantaneous - poll briefly rather than asserting immediately.
        for (var i = 0; i < 50 && IsProcessAlive(pid); i++)
        {
            await Task.Delay(100);
        }

        Assert.False(IsProcessAlive(pid), $"ffmpeg (pid {pid}) should have been killed on cancellation, but is still running.");
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // No process with this id - it's gone.
            return false;
        }
    }
}
