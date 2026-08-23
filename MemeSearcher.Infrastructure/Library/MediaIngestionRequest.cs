namespace MemeSearcher.Infrastructure.Library;

/// <summary>
/// One media item can be assembled from separately named files (addendum §7) - a video and an
/// unrelated-filename subtitle track, or a bare transcript with no media at all yet. TranscriptPath
/// is also optional as of Milestone 3: MediaPath alone (with no transcript file) transcribes the
/// media directly via ITranscriptionProvider instead of parsing an existing file. At least one of
/// MediaPath/TranscriptPath must be given - MediaIngestionService validates this.
/// </summary>
public record MediaIngestionRequest(string? MediaPath, string? TranscriptPath, string Language, string? Title = null);

public enum MediaIngestionOutcome
{
    Imported,
    AlreadyIndexed,
}

public record MediaIngestionResult(MediaIngestionOutcome Outcome, Core.Models.Media Media);
