namespace MemeSearcher.Core.Models;

public class Media
{
    public Guid Id { get; set; }

    /// <summary>
    /// Primary identity/display path (whichever file was hashed for content identity - the media
    /// file if one was given, otherwise the transcript). Not necessarily playable - see
    /// <see cref="MediaFilePath"/> for that.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The actual audio/video file, if one was imported. Null for transcript-only imports -
    /// addendum §28 treats that as a valid partial state, not an error. Result playback (handoff
    /// §21-22) must resolve through this field, not <see cref="Path"/>, or a transcript-only
    /// result would try to hand a subtitle file to a media player.
    /// </summary>
    public string? MediaFilePath { get; set; }

    public string? SourceUrl { get; set; }
    public string? Title { get; set; }
    public TimeSpan Duration { get; set; }
    public required string Language { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int ProcessingVersion { get; set; }

    /// <summary>
    /// What produced this item's transcript, when it came from a transcription run (#24). Null for
    /// transcript-only imports, where the answer is "some other tool, elsewhere".
    ///
    /// Stored on the media row rather than read back from settings because settings change and
    /// already-ingested data must not silently re-describe itself: a corpus half-transcribed with
    /// `tiny` and half with `large-v3` is not internally comparable, and this is the only thing
    /// that says so.
    /// </summary>
    public string? TranscriptionModel { get; set; }

    public string? TranscriptionDevice { get; set; }

    public string? TranscriptionComputeType { get; set; }

    public long FileSize { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public required string ContentHash { get; set; }

    /// <summary>
    /// Addendum §13: manual search scope must be a first-class, persistent feature - a media item
    /// excluded from search stays excluded across restarts, not just for the current session.
    /// Defaults to true (via the EF fluent default - see MemeSearcherDbContext) so importing new
    /// media doesn't silently narrow the scope the user already had - only an explicit uncheck does.
    /// </summary>
    public bool IsSelectedForSearch { get; set; } = true;
}
