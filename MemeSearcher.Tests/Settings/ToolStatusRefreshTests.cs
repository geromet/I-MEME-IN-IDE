using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Tests.TestDoubles;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.Settings;

public class ToolStatusRefreshTests
{
    [Fact]
    public async Task RefreshToolStatusesAsync_UsesRegistryAndSortsToolsByName()
    {
        var store = new InMemorySettingsStore();
        var settings = new SettingsViewModel(
            new SettingsRegistry([]),
            store,
            new FakeToolRegistry(
            [
                new FakeToolLocator("yt-dlp", new ExternalToolStatus(true, "/usr/bin/yt-dlp", "2026.08.19", null)),
                new FakeToolLocator("ffmpeg", new ExternalToolStatus(false, null, null, "Install FFmpeg")),
            ]));

        await settings.RefreshToolStatusesAsync();

        Assert.Equal(["ffmpeg", "yt-dlp"], settings.ToolStatuses.Select(status => status.Name));
        Assert.Equal("Not installed", settings.ToolStatuses[0].Summary);
        Assert.Contains("2026.08.19", settings.ToolStatuses[1].Summary);
    }

    private sealed class FakeToolRegistry(IReadOnlyList<IExternalToolLocator> locators) : IToolRegistry
    {
        public IReadOnlyList<IExternalToolLocator> Locators { get; } = locators;
    }

    private sealed class FakeToolLocator(string toolName, ExternalToolStatus status) : IExternalToolLocator
    {
        public string ToolName { get; } = toolName;

        public Task<ExternalToolStatus> LocateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);
    }
}
