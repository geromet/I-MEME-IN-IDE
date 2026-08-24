using System.Diagnostics;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Shared cancel-and-actually-kill behaviour for every external process this app spawns (#14,
/// addendum handoff §28). Before this, `await process.WaitForExitAsync(cancellationToken)` was
/// used directly at all four call sites (tool version probes, ffmpeg, whisperx, mfa): cancelling
/// the token abandons the awaiting Task, but the OS child process itself keeps running as an
/// orphan - a cancelled 40-minute whisperx run kept burning the GPU after the "job" it belonged to
/// had already reported cancelled.
/// </summary>
public static class ProcessRunner
{
    public static async Task WaitForExitAndKillOnCancelAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort - the process may have exited between the check above and the kill.
        }
    }
}
