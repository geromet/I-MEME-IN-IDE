using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Infrastructure.Ffmpeg;

public sealed record WaveformSampleResult(
    bool Success,
    IReadOnlyList<double> Amplitudes,
    double DecodeStartSeconds,
    double DecodeEndSeconds,
    double MatchStartSeconds,
    double MatchEndSeconds,
    string? Error)
{
    public static WaveformSampleResult Unavailable(string error, double startSeconds, double endSeconds) =>
        new(false, [], startSeconds, endSeconds, startSeconds, endSeconds, error);
}

/// <summary>
/// #35 presentation-only waveform sampling. It decodes only the selected interval plus a small
/// context window, downsamples to mono PCM and reduces that bounded data to a fixed-size peak
/// envelope. Nothing is persisted and no whole-library/whole-file waveform cache is created.
/// </summary>
public sealed class WaveformSampler([FromKeyedServices("ffmpeg")] IExternalToolLocator toolLocator)
{
    public const double PaddingSeconds = 0.25;
    private const int SampleRate = 200;
    private const int MaxBars = 96;

    public async Task<WaveformSampleResult> SampleAsync(
        string mediaPath,
        double startSeconds,
        double endSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds < 0 || endSeconds <= startSeconds)
        {
            return WaveformSampleResult.Unavailable("Waveform unavailable: invalid media interval.", startSeconds, endSeconds);
        }

        if (!File.Exists(mediaPath))
        {
            return WaveformSampleResult.Unavailable($"Waveform unavailable: media file not found: {mediaPath}", startSeconds, endSeconds);
        }

        var status = await toolLocator.LocateAsync(cancellationToken);
        if (!status.IsInstalled || string.IsNullOrWhiteSpace(status.ExecutablePath))
        {
            return WaveformSampleResult.Unavailable($"Waveform unavailable: ffmpeg is not available: {status.Error}", startSeconds, endSeconds);
        }

        var decodeStart = Math.Max(0, startSeconds - PaddingSeconds);
        var decodeEnd = endSeconds + PaddingSeconds;
        var duration = decodeEnd - decodeStart;
        var workDir = Directory.CreateTempSubdirectory("memesearcher-waveform-").FullName;
        var rawPath = Path.Combine(workDir, "waveform.f32le");

        try
        {
            var startInfo = new ProcessStartInfo(status.ExecutablePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (var argument in new[]
            {
                "-nostdin", "-hide_banner", "-loglevel", "error",
                "-ss", decodeStart.ToString("R", CultureInfo.InvariantCulture),
                "-i", mediaPath,
                "-t", duration.ToString("R", CultureInfo.InvariantCulture),
                "-vn", "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
                "-acodec", "pcm_f32le", "-f", "f32le", "-y", rawPath,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo.ApplyToolEnvironment(status))
                ?? throw new InvalidOperationException($"Failed to start '{status.ExecutablePath}'.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, cancellationToken);
            await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(rawPath))
            {
                return WaveformSampleResult.Unavailable(
                    $"Waveform unavailable: ffmpeg exited with code {process.ExitCode}: {TrimError(stderr)}",
                    startSeconds,
                    endSeconds);
            }

            var bytes = await File.ReadAllBytesAsync(rawPath, cancellationToken);
            if (bytes.Length < sizeof(float))
            {
                return WaveformSampleResult.Unavailable("Waveform unavailable: ffmpeg decoded no audio samples.", startSeconds, endSeconds);
            }

            var sampleCount = bytes.Length / sizeof(float);
            var values = new double[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
                values[i] = Math.Abs(BitConverter.Int32BitsToSingle(bits));
            }

            var barCount = Math.Min(MaxBars, sampleCount);
            var envelope = new double[barCount];
            var peak = 0d;
            for (var bar = 0; bar < barCount; bar++)
            {
                var from = bar * sampleCount / barCount;
                var to = Math.Max(from + 1, (bar + 1) * sampleCount / barCount);
                var value = 0d;
                for (var sample = from; sample < to && sample < sampleCount; sample++)
                {
                    value = Math.Max(value, values[sample]);
                }

                envelope[bar] = value;
                peak = Math.Max(peak, value);
            }

            if (peak > 0)
            {
                for (var i = 0; i < envelope.Length; i++)
                {
                    envelope[i] = Math.Clamp(envelope[i] / peak, 0, 1);
                }
            }

            return new WaveformSampleResult(
                true,
                envelope,
                decodeStart,
                decodeEnd,
                startSeconds,
                endSeconds,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return WaveformSampleResult.Unavailable($"Waveform unavailable: {ex.Message}", startSeconds, endSeconds);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; waveform data is temporary presentation state.
            }
        }
    }

    private static string TrimError(string error)
    {
        const int maxLength = 1500;
        error = error.Trim();
        return error.Length <= maxLength ? error : error[^maxLength..];
    }
}
