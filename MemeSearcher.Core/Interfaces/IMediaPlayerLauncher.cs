namespace MemeSearcher.Core.Interfaces;

/// <summary>
/// SeekedToTimestamp is false when the media opened successfully but no player that understands
/// a start-time argument could be found - the file plays, just not from the right spot.
/// </summary>
public record MediaLaunchResult(bool Success, bool SeekedToTimestamp, string? Error);

/// <summary>
/// handoff §22: in-app playback isn't a milestone-1 requirement, but "Result -> Media + Start"
/// must be enough to open a player at that position. This is that seam.
/// </summary>
public interface IMediaPlayerLauncher
{
    Task<MediaLaunchResult> OpenAsync(string mediaPath, double startSeconds, CancellationToken cancellationToken = default);
}
