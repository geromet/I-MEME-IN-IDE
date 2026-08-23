using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.Tests.Transcription;

/// <summary>
/// whisperx isn't installed on the machine these tests run on, so the CLI invocation itself can't
/// be exercised end-to-end here (unlike EspeakPhonemizerTests/ExternalMediaPlayerLauncherTests,
/// which run against real binaries). This tests the JSON parsing against whisperx's documented
/// `--output_format json` shape instead, which is the part most likely to silently drift.
/// </summary>
public class WhisperXTranscriptionProviderTests
{
    [Fact]
    public void ParseSegments_ExtractsStartEndAndText()
    {
        const string json = """
            {
                "segments": [
                    {
                        "start": 0.03,
                        "end": 2.06,
                        "text": " Hello world.",
                        "words": [
                            {"word": "Hello", "start": 0.03, "end": 0.5, "score": 0.9},
                            {"word": "world.", "start": 0.6, "end": 1.0, "score": 0.85}
                        ]
                    },
                    {
                        "start": 2.5,
                        "end": 4.1,
                        "text": " This is a test.",
                        "words": []
                    }
                ],
                "word_segments": [],
                "language": "en"
            }
            """;

        var segments = WhisperXTranscriptionProvider.ParseSegments(json);

        Assert.Equal(2, segments.Count);
        Assert.Equal(0.03, segments[0].StartSeconds);
        Assert.Equal(2.06, segments[0].EndSeconds);
        Assert.Equal("Hello world.", segments[0].Text); // leading space trimmed
        Assert.Equal("This is a test.", segments[1].Text);
    }

    [Fact]
    public void ParseSegments_SkipsSegmentsWithEmptyText()
    {
        const string json = """
            {"segments": [{"start": 0, "end": 1, "text": "   "}, {"start": 1, "end": 2, "text": "real text"}]}
            """;

        var segments = WhisperXTranscriptionProvider.ParseSegments(json);

        var segment = Assert.Single(segments);
        Assert.Equal("real text", segment.Text);
    }

    [Fact]
    public void ParseSegments_MissingSegmentsArrayReturnsEmpty()
    {
        var segments = WhisperXTranscriptionProvider.ParseSegments("{}");

        Assert.Empty(segments);
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsAClearErrorWhenWhisperXIsNotInstalled()
    {
        var locator = new WhisperXToolLocator();
        var status = await locator.LocateAsync();
        if (status.IsInstalled)
        {
            return; // Can't exercise the "tool missing" path on a machine that has it.
        }

        var provider = new WhisperXTranscriptionProvider(locator);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.TranscribeAsync("/some/media.mp4", "en", CancellationToken.None));

        Assert.Contains("whisperx is not available", ex.Message);
    }
}
