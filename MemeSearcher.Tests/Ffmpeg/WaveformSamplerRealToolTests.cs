using System.Diagnostics;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Ffmpeg;

public class WaveformSamplerRealToolTests
{
    private sealed class PathLocator(string executable) : IExternalToolLocator
    {
        public string ToolName => "ffmpeg";
        public Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalToolStatus(true, executable, null, null));
    }

    [Fact]
    public async Task SampleAsync_RealFfmpeg_DecodesOnlyRequestedIntervalPlusPadding()
    {
        var ffmpeg = FindOnPath("ffmpeg");
        Assert.NotNull(ffmpeg);

        var temp = Directory.CreateTempSubdirectory("memesearcher-waveform-test-").FullName;
        try
        {
            var mediaPath = Path.Combine(temp, "fixture.wav");
            await GenerateSilenceToneSilenceAsync(ffmpeg!, mediaPath);

            var sampler = new WaveformSampler(new PathLocator(ffmpeg!));
            var silent = await sampler.SampleAsync(mediaPath, 0.20, 0.60);
            var tone = await sampler.SampleAsync(mediaPath, 1.20, 1.60);

            Assert.True(silent.Success, silent.Error);
            Assert.True(tone.Success, tone.Error);
            Assert.Equal(0, silent.DecodeStartSeconds, 6); // 0.20 - 0.25 clamps at file start.
            Assert.Equal(0.85, silent.DecodeEndSeconds, 6);
            Assert.Equal(0.95, tone.DecodeStartSeconds, 6);
            Assert.Equal(1.85, tone.DecodeEndSeconds, 6);
            Assert.InRange(tone.Amplitudes.Count, 1, 96);
            Assert.All(silent.Amplitudes, amplitude => Assert.InRange(amplitude, 0, 0.001));
            Assert.Contains(tone.Amplitudes, amplitude => amplitude > 0.5);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task SampleAsync_BadMedia_ReturnsExplicitUnavailableState()
    {
        var ffmpeg = FindOnPath("ffmpeg");
        Assert.NotNull(ffmpeg);

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "not media");
            var sampler = new WaveformSampler(new PathLocator(ffmpeg!));
            var result = await sampler.SampleAsync(path, 0, 0.5);

            Assert.False(result.Success);
            Assert.Contains("Waveform unavailable", result.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task GenerateSilenceToneSilenceAsync(string ffmpeg, string outputPath)
    {
        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in new[]
        {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "anullsrc=r=8000:cl=mono:d=1",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=8000:duration=1",
            "-f", "lavfi", "-i", "anullsrc=r=8000:cl=mono:d=1",
            "-filter_complex", "[0:a][1:a][2:a]concat=n=3:v=0:a=1[out]",
            "-map", "[out]", "-c:a", "pcm_s16le", outputPath,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ffmpeg fixture generator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await ProcessRunner.WaitForExitAndKillOnCancelAsync(process, CancellationToken.None);
        await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0 && File.Exists(outputPath), error);
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
