using System.Diagnostics;

namespace MemeSearcher.Infrastructure.Processes;

/// <summary>
/// Answers "does this machine have a usable NVIDIA GPU" by asking `nvidia-smi`, cached for the
/// process lifetime.
///
/// This is a proxy, not proof: the authoritative answer is `torch.cuda.is_available()` inside
/// whisperx's own Python environment, which cannot be asked without paying torch's multi-second
/// import cost. nvidia-smi present and exiting cleanly means a driver and a device exist, which is
/// the failure this is actually guarding against - selecting GPU on a machine that has no GPU at
/// all. A machine with a driver but a broken torch/CUDA pairing will still fail inside whisperx,
/// and that is acceptable: the point is to catch the common case cheaply, not to make GPU failure
/// impossible.
/// </summary>
public class CudaAvailabilityProbe
{
    private bool? _cached;
    private readonly Lock _gate = new();

    /// <summary>Virtual so tests can exercise both the GPU and CPU-only branches on one machine.</summary>
    public virtual bool IsCudaAvailable()
    {
        lock (_gate)
        {
            return _cached ??= Probe();
        }
    }

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi", "-L")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // nvidia-smi can exit 0 while listing no devices; require an actual GPU line.
            return process.ExitCode == 0 && stdout.Contains("GPU", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Not installed, not on PATH, or not permitted - all mean "no usable GPU here".
            return false;
        }
    }
}
