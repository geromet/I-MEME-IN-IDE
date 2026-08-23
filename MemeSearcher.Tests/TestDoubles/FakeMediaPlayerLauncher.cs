using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Tests.TestDoubles;

/// <summary>
/// Deterministic stand-in for IMediaPlayerLauncher - ViewModel tests should verify the correct
/// path/timestamp get passed through, not depend on a real player process actually launching.
/// ExternalMediaPlayerLauncher itself gets its own dedicated real-process test.
/// </summary>
public class FakeMediaPlayerLauncher : IMediaPlayerLauncher
{
    public string? LastMediaPath { get; private set; }
    public double? LastStartSeconds { get; private set; }
    public int CallCount { get; private set; }

    public MediaLaunchResult Result { get; set; } = new(true, true, null);

    public Task<MediaLaunchResult> OpenAsync(string mediaPath, double startSeconds, CancellationToken cancellationToken = default)
    {
        LastMediaPath = mediaPath;
        LastStartSeconds = startSeconds;
        CallCount++;
        return Task.FromResult(Result);
    }
}
