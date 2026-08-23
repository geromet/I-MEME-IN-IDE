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

    public long FileSize { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public required string ContentHash { get; set; }
}
