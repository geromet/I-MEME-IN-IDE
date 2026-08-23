using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;

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

        // Milestone 5: word-level timing from the same JSON.
        Assert.NotNull(segments[0].Words);
        Assert.Equal(2, segments[0].Words!.Count);
        Assert.Equal("Hello", segments[0].Words![0].Text);
        Assert.Equal(0.03, segments[0].Words![0].StartSeconds);
        Assert.Equal(0.5, segments[0].Words![0].EndSeconds);
        Assert.Null(segments[1].Words); // empty "words" array -> no usable word data
    }

    [Fact]
    public void ParseSegments_SkipsWordsThatFailedAlignment()
    {
        // Real WhisperX gotcha: a word can appear with just "word" and no "start"/"end" when
        // alignment failed for it specifically.
        const string json = """
            {"segments": [{"start": 0, "end": 2, "text": "a b c", "words": [
                {"word": "a", "start": 0.0, "end": 0.3},
                {"word": "b"},
                {"word": "c", "start": 0.6, "end": 0.9}
            ]}]}
            """;

        var segments = WhisperXTranscriptionProvider.ParseSegments(json);

        var words = Assert.Single(segments).Words;
        Assert.NotNull(words);
        Assert.Equal(2, words!.Count);
        Assert.Equal(["a", "c"], words.Select(w => w.Text));
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

        var provider = new WhisperXTranscriptionProvider(
            locator, new InMemorySettingsStore(), new WhisperXSettings(new CudaAvailabilityProbe()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.TranscribeAsync("/some/media.mp4", "en", CancellationToken.None));

        Assert.Contains("whisperx is not available", ex.Message);
    }

    [Fact]
    public async Task WhisperXAlignmentProvider_ThrowsAClearErrorWhenWhisperXIsNotInstalled()
    {
        var locator = new WhisperXToolLocator();
        var status = await locator.LocateAsync();
        if (status.IsInstalled)
        {
            return;
        }

        var provider = new WhisperXAlignmentProvider(locator);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AlignAsync("/some/media.mp4", "hello world", CancellationToken.None));

        Assert.Contains("whisperx is not available", ex.Message);
    }
}
