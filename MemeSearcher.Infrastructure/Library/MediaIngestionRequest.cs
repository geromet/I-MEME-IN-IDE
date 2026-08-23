using MemeSearcher.Core.Phonetics;

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

/// <summary>Milestone 6: result of MediaIngestionService.RealignAsync - how many words/phones actually got updated.</summary>
/// <summary>
/// Outcome of a realignment. <see cref="TotalWordCount"/> is reported because coverage is
/// information the user needs (#30): an aligner routinely fails to place some words, and
/// "1545 words updated" means nothing without knowing whether that was out of 1600 or 4000.
/// </summary>
public record RealignmentResult(
    int UpdatedWordCount,
    int UpdatedPhoneCount,
    int TotalWordCount,
    PhonemeCoverage PhonemeCoverage)
{
    public double CoveragePercent => TotalWordCount == 0 ? 0 : 100.0 * UpdatedWordCount / TotalWordCount;
}
