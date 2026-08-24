namespace MemeSearcher.Core.Models;

/// <summary>
/// Which form a yt-dlp-sourced Media item was actually fetched as (#27) - null on Media for every
/// non-yt-dlp import, since the question doesn't apply there. Recorded per media, not read from the
/// current setting, because the setting can change after the fact and a corpus item's fetched form
/// determines what can later be exported (video-with-picture clip export needs Video; Audio is
/// smaller/faster and is all the phonetic pipeline itself needs).
/// </summary>
public enum YtDlpMediaKind
{
    Audio,
    Video,
}
