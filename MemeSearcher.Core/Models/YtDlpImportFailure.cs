namespace MemeSearcher.Core.Models;

/// <summary>
/// A yt-dlp-sourced video that failed to download/import, persisted rather than only narrated in a
/// job's rolling status line (#27). A private/geoblocked/age-gated video fails the same way on
/// every future re-run of the same channel; without a persisted record, "enumerate, diff against
/// stored ids, download only what's new" (the issue's own incremental-re-run requirement) has
/// nothing to diff a permanent failure against, and the same opaque "N failed" count recurs forever
/// with no way to tell a transient network blip from a video that will never succeed.
/// </summary>
public class YtDlpImportFailure
{
    public Guid Id { get; set; }

    /// <summary>yt-dlp's own video id - the same identity Media.VideoId uses, so a later successful import of the same video is what should retire this row, not a cascade or a cleanup job.</summary>
    public required string VideoId { get; set; }

    public string? Title { get; set; }

    public required string SourceUrl { get; set; }

    /// <summary>The channel/playlist URL this failure was discovered under - null if the video was queued directly rather than via enumeration.</summary>
    public string? PlaylistUrl { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset FailedAt { get; set; }

    /// <summary>How many times this exact video has failed across separate runs - incremented, not reset, on each repeat failure.</summary>
    public int AttemptCount { get; set; }
}
