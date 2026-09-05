using System.Diagnostics;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Ffmpeg;

public class VideoComposerRendererRealToolTests
{
    [Fact]
    public async Task RenderAsync_ExecutesPlannerArgumentsWithRealFfmpeg()
    {
        var locator = new FFmpegToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory("meme renderer real ffmpeg ").FullName;
        try
        {
            var source = Path.Combine(tempDir, "source clip.mp4");
            await GenerateVideoAsync(status.ExecutablePath!, source);

            var output = Path.Combine(tempDir, "render output.mp4");
            var plan = VideoComposerRenderPlanner.Create(
                [new VideoRenderInput(source, 0.1, 0.7)],
                output);

            var result = await new VideoComposerRenderer(locator).RenderAsync(plan);

            Assert.True(result.Success, result.Error);
            Assert.Equal(output, result.OutputPath);
            Assert.True(File.Exists(output));
            Assert.True(new FileInfo(output).Length > 0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_FailureDeletesStaleOrPartialOutputAndReturnsStderr()
    {
        var locator = new FFmpegToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory("meme renderer failure ").FullName;
        try
        {
            var invalidSource = Path.Combine(tempDir, "not media.txt");
            await File.WriteAllTextAsync(invalidSource, "this is not a media container");
            var output = Path.Combine(tempDir, "failed output.mp4");
            await File.WriteAllTextAsync(output, "stale output must not survive");

            var plan = VideoComposerRenderPlanner.Create(
                [new VideoRenderInput(invalidSource, 0, 1)],
                output);

            var result = await new VideoComposerRenderer(locator).RenderAsync(plan);

            Assert.False(result.Success);
            Assert.Null(result.OutputPath);
            Assert.Contains("ffmpeg exited with code", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_CancellationKillsRealFfmpegAndDeletesPartialOutput()
    {
        var locator = new FFmpegToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory("meme renderer cancel ").FullName;
        try
        {
            var output = Path.Combine(tempDir, "cancelled output.mp4");
            await File.WriteAllTextAsync(output, "stale output must not survive");

            // -re makes this synthetic real-ffmpeg render progress in wall-clock time, giving the
            // cancellation path a deterministic window without a large fixture or busy loop.
            var arguments = new List<string>
            {
                "-y",
                "-re",
                "-f", "lavfi", "-i", "color=c=black:s=160x120:d=30",
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                output,
            };
            var plan = new VideoRenderPlan(arguments, [], output, null);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new VideoComposerRenderer(locator).RenderAsync(plan, cts.Token));

            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static Task GenerateVideoAsync(string ffmpegPath, string outputPath) =>
        RunFfmpegAsync(
            ffmpegPath,
            [
                "-y",
                "-f", "lavfi", "-i", "color=c=black:s=160x120:d=1",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                "-shortest",
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                "-c:a", "aac",
                outputPath,
            ]);

    private static async Task RunFfmpegAsync(string ffmpegPath, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, error);
    }
}
