namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>
/// One video from `yt-dlp --flat-playlist --dump-json` (#27). Deliberately thin: flat-playlist mode
/// skips per-video extraction, so fields a full extraction would give (upload date in particular)
/// simply aren't present on these rows - verified against real yt-dlp output against both a channel
/// URL and a playlist URL, not assumed. Channel is read from the row's own "playlist_channel"
/// (present and populated for both URL shapes) rather than "channel" (present only for playlist
/// URLs, absent entirely for channel URLs).
/// </summary>
public record YtDlpVideoEntry(string VideoId, string Title, string? Channel, string Url);
