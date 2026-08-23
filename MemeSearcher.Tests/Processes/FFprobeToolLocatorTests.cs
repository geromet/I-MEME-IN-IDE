using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.Processes;

/// <summary>ffprobe is confirmed installed in this environment (part of the system FFmpeg package).</summary>
public class FFprobeToolLocatorTests
{
    [Fact]
    public async Task LocateAsync_FindsTheInstalledFfprobeAndReportsAVersion()
    {
        var status = await new FFprobeToolLocator().LocateAsync();

        Assert.True(status.IsInstalled);
        Assert.NotNull(status.ExecutablePath);
        Assert.Contains("ffprobe", status.Version, StringComparison.OrdinalIgnoreCase);
    }
}
