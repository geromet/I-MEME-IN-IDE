using MemeSearcher.Infrastructure.Processes;

namespace MemeSearcher.Tests.YtDlp;

/// <summary>yt-dlp is confirmed installed in this environment. Mirrors FFprobeToolLocatorTests's own real-invocation pattern.</summary>
public class YtDlpToolLocatorTests
{
    [Fact]
    public async Task LocateAsync_FindsTheInstalledYtDlpAndReportsAVersion()
    {
        var status = await new YtDlpToolLocator().LocateAsync();

        Assert.True(status.IsInstalled);
        Assert.NotNull(status.ExecutablePath);
        // yt-dlp's own --version output is a bare "YYYY.MM.DD" release date, not free text - unlike
        // every other locator's version string, there's no tool name to look for inside it.
        Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}$", status.Version);
    }

    [Theory]
    [InlineData("2026.08.19", "2026.08.19", false)] // exactly today - not stale
    [InlineData("2026.02.19", "2026.08.19", true)] // 181 days earlier - stale
    [InlineData("2026.06.19", "2026.08.19", false)] // 61 days earlier - not stale
    public void IsVersionStale_ComparesTheDatedVersionAgainstToday(string version, string today, bool expectedStale)
    {
        var isStale = YtDlpToolLocator.IsVersionStale(version, DateOnly.ParseExact(today, "yyyy.MM.dd"));

        Assert.Equal(expectedStale, isStale);
    }

    [Fact]
    public void IsVersionStale_UnparsableVersion_IsNeverStale()
    {
        // A version string in a shape this method doesn't recognize shouldn't be reported as
        // definitely stale - that would be a false claim about something actually unknown.
        Assert.False(YtDlpToolLocator.IsVersionStale("not-a-version", DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.False(YtDlpToolLocator.IsVersionStale(null, DateOnly.FromDateTime(DateTime.UtcNow)));
    }
}
