using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Ffmpeg;

public class MediaMetadataProbeTests
{
    [Fact]
    public void ParseDuration_ExtractsSecondsFromRealFfprobeOutputShape()
    {
        // Exact shape confirmed by running ffprobe against a real generated file.
        const string json = """
            {
                "format": {
                    "duration": "2.000000"
                }
            }
            """;

        var duration = MediaMetadataProbe.ParseDuration(json);

        Assert.Equal(TimeSpan.FromSeconds(2), duration);
    }

    [Fact]
    public void ParseDuration_MissingFieldsReturnsNull()
    {
        Assert.Null(MediaMetadataProbe.ParseDuration("{}"));
        Assert.Null(MediaMetadataProbe.ParseDuration("""{"format": {}}"""));
    }

    [Fact]
    public void ParseDuration_InvalidJsonReturnsNullInsteadOfThrowing()
    {
        Assert.Null(MediaMetadataProbe.ParseDuration("not json"));
    }

    [Fact]
    public async Task TryGetDurationAsync_ReturnsNullForANonexistentFile()
    {
        var probe = new MediaMetadataProbe(new FFprobeToolLocator());

        var duration = await probe.TryGetDurationAsync("/no/such/file.mp4");

        Assert.Null(duration);
    }

    [Fact]
    public async Task TryGetDurationAsync_ReturnsTheActualDurationOfARealFile()
    {
        var locator = new FFprobeToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return;
        }

        // Generate a real 2-second silent audio file with ffmpeg so this exercises the actual
        // ffprobe process boundary, not just the JSON parser.
        var ffmpegPath = ProcessPathResolver.FindOnPath(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (ffmpegPath is null)
        {
            return;
        }

        var wavPath = Path.Combine(Path.GetTempPath(), $"memesearcher-probetest-{Guid.NewGuid():N}.wav");
        try
        {
            var generate = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpegPath)
            {
                ArgumentList = { "-y", "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono", "-t", "2", wavPath },
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            await generate.WaitForExitAsync();

            var probe = new MediaMetadataProbe(locator);
            var duration = await probe.TryGetDurationAsync(wavPath);

            Assert.NotNull(duration);
            Assert.Equal(2.0, duration!.Value.TotalSeconds, precision: 1);
        }
        finally
        {
            File.Delete(wavPath);
        }
    }
}
