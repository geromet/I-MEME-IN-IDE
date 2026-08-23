using System.Diagnostics;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Ffmpeg;

/// <summary>
/// Exercises the real ffmpeg binary (confirmed installed in this environment) rather than mocking
/// the process boundary - generates a real 5-second audio file, extracts a clip from it, and
/// verifies the *actual* output duration via ffprobe. Skips (returns early) if ffmpeg isn't
/// installed.
/// </summary>
public class FFmpegClipExtractorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-clipextract-test-").FullName;

    private async Task<string?> GenerateTestAudioAsync(double durationSeconds, string fileName)
    {
        var ffmpegLocator = new FFmpegToolLocator();
        var status = await ffmpegLocator.LocateAsync();
        if (!status.IsInstalled)
        {
            return null;
        }

        var path = Path.Combine(_tempDir, fileName);
        var duration = durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var process = Process.Start(new ProcessStartInfo(status.ExecutablePath!)
        {
            ArgumentList = { "-y", "-f", "lavfi", "-i", $"sine=frequency=440:duration={duration}", path },
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        await process.WaitForExitAsync();

        return File.Exists(path) ? path : null;
    }

    private static async Task<double?> GetActualDurationAsync(string path)
    {
        var probe = new MediaMetadataProbe(new FFprobeToolLocator());
        var duration = await probe.TryGetDurationAsync(path);
        return duration?.TotalSeconds;
    }

    [Fact]
    public async Task ExtractAsync_ProducesAClipOfApproximatelyTheRequestedDuration()
    {
        var sourcePath = await GenerateTestAudioAsync(5, "source.wav");
        if (sourcePath is null)
        {
            return;
        }

        var extractor = new FFmpegClipExtractor(new FFmpegToolLocator());
        var outputPath = Path.Combine(_tempDir, "clip.wav");

        var result = await extractor.ExtractAsync(sourcePath, 1.0, 3.0, outputPath);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(outputPath));

        var actualDuration = await GetActualDurationAsync(outputPath);
        Assert.NotNull(actualDuration);
        Assert.InRange(actualDuration!.Value, 1.5, 2.5); // ~2s requested, allow encoding slack
    }

    [Fact]
    public async Task ExtractAsync_MissingSourceFileReturnsFailure()
    {
        var locator = new FFmpegToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return;
        }

        var extractor = new FFmpegClipExtractor(locator);

        var result = await extractor.ExtractAsync("/no/such/file.wav", 0, 1, Path.Combine(_tempDir, "out.wav"));

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ExtractCompositeAsync_ConcatenatesComponentsFromDifferentFilesIntoOneOutput()
    {
        var sourceA = await GenerateTestAudioAsync(3, "a.wav");
        var sourceB = await GenerateTestAudioAsync(3, "b.wav");
        if (sourceA is null || sourceB is null)
        {
            return;
        }

        var extractor = new FFmpegClipExtractor(new FFmpegToolLocator());
        var outputPath = Path.Combine(_tempDir, "assembled.wav");

        var result = await extractor.ExtractCompositeAsync(
            [(sourceA, 0.0, 1.0), (sourceB, 0.0, 1.0)], outputPath);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(outputPath));

        var actualDuration = await GetActualDurationAsync(outputPath);
        Assert.NotNull(actualDuration);
        Assert.InRange(actualDuration!.Value, 1.5, 2.5); // ~1s + 1s requested, allow encoding slack
    }

    [Fact]
    public async Task ExtractCompositeAsync_EmptyComponentListReturnsFailure()
    {
        var locator = new FFmpegToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return;
        }

        var extractor = new FFmpegClipExtractor(locator);

        var result = await extractor.ExtractCompositeAsync([], Path.Combine(_tempDir, "out.wav"));

        Assert.False(result.Success);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }
}
