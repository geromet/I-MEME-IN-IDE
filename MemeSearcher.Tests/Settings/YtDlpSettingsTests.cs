using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Tests.TestDoubles;

namespace MemeSearcher.Tests.Settings;

public class YtDlpSettingsTests
{
    [Fact]
    public void ResolveMediaKind_DefaultsToAudio()
    {
        var category = new YtDlpSettings();
        var store = new InMemorySettingsStore();

        Assert.Equal(YtDlpMediaKind.Audio, category.ResolveMediaKind(store));
    }

    [Fact]
    public void ResolveMediaKind_HonorsVideoWhenChosen()
    {
        var category = new YtDlpSettings();
        var store = new InMemorySettingsStore();
        store.Set(YtDlpSettings.MediaKind, YtDlpSettings.VideoValue);

        Assert.Equal(YtDlpMediaKind.Video, category.ResolveMediaKind(store));
    }

    [Fact]
    public void ResolveDownloadDirectory_DefaultsUnderApplicationData_WhenNothingConfigured()
    {
        var category = new YtDlpSettings();
        var store = new InMemorySettingsStore();

        var resolved = category.ResolveDownloadDirectory(store);

        Assert.Contains("MemeSearcher", resolved);
        Assert.Contains("ytdlp-downloads", resolved);
    }

    [Fact]
    public void ResolveDownloadDirectory_HonorsAnExplicitlyConfiguredPath()
    {
        var category = new YtDlpSettings();
        var store = new InMemorySettingsStore();
        store.Set(YtDlpSettings.DownloadLocation, "/some/custom/path");

        Assert.Equal("/some/custom/path", category.ResolveDownloadDirectory(store));
    }

    [Fact]
    public void Validate_AcceptsTheShippedDefaults()
    {
        var category = new YtDlpSettings();
        var store = new InMemorySettingsStore();

        Assert.Null(category.Validate(store));
    }

    [Fact]
    public void Validate_RejectsAPathWhoseParentDoesNotExistEither()
    {
        var category = new YtDlpSettings();
        var store = new InMemorySettingsStore();
        store.Set(YtDlpSettings.DownloadLocation, "/definitely/does/not/exist/anywhere");

        Assert.Contains("does not exist", category.Validate(store));
    }

    [Fact]
    public void EveryDefaultValueIsLegalForItsOwnDefinition()
    {
        var category = new YtDlpSettings();

        Assert.All(category.Settings, s => Assert.True(
            s.IsValidValue(s.DefaultValue), $"'{s.Key}' has a default outside its own choice list."));
    }
}
