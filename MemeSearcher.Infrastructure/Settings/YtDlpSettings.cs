using MemeSearcher.Core.Models;
using MemeSearcher.Core.Settings;

namespace MemeSearcher.Infrastructure.Settings;

/// <summary>
/// The yt-dlp settings category (#27): audio vs. video preference, and where downloaded files land.
/// </summary>
public class YtDlpSettings : ISettingsCategory
{
    public const string CategoryName = "yt-dlp";
    public const string AudioValue = "audio";
    public const string VideoValue = "video";

    public static readonly SettingDefinition MediaKind = new(
        Key: "ytdlp.media_kind",
        Category: CategoryName,
        DisplayName: "Download as",
        Description: "Audio-only downloads are smaller and faster, and are all this app's phonetic "
                     + "search needs. Choose Video to keep a playable copy alongside the transcript.",
        Kind: SettingKind.Choice,
        DefaultValue: AudioValue,
        Choices: [new(AudioValue, "Audio only"), new(VideoValue, "Video")]);

    public static readonly SettingDefinition DownloadLocation = new(
        Key: "ytdlp.download_location",
        Category: CategoryName,
        DisplayName: "Download location",
        Description: "Where downloaded files are saved. Leave empty to use the default location "
                     + "under the app's data directory.",
        Kind: SettingKind.Text,
        DefaultValue: "");

    public string Name => CategoryName;

    public string Description => "Downloading YouTube channels and playlists via yt-dlp.";

    public int Order => 30;

    public IReadOnlyList<SettingDefinition> Settings => [MediaKind, DownloadLocation];

    public YtDlpMediaKind ResolveMediaKind(ISettingsStore store) =>
        store.Get(MediaKind) == VideoValue ? YtDlpMediaKind.Video : YtDlpMediaKind.Audio;

    /// <summary>
    /// Not created here - callers create it on demand right before a download, the same way
    /// ExternalToolSettings leaves executable-path resolution side-effect-free.
    /// </summary>
    public string ResolveDownloadDirectory(ISettingsStore store)
    {
        var configured = store.Get(DownloadLocation).Trim();
        if (configured.Length > 0)
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemeSearcher", "ytdlp-downloads");
    }

    public string? Validate(ISettingsStore store)
    {
        var configured = store.Get(DownloadLocation).Trim();
        if (configured.Length == 0)
        {
            return null;
        }

        // No side effect here (unlike ResolveDownloadDirectory's callers, which create the
        // directory right before downloading) - validation runs far more often than a download
        // does, and shouldn't be creating directories on every settings-panel render.
        if (Directory.Exists(configured) || Directory.GetParent(configured) is { Exists: true })
        {
            return null;
        }

        return $"Download location '{configured}' does not exist and its parent directory does not either.";
    }
}
