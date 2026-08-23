using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.Tests.Transcription;

public class PlainTextTranscriptParserTests
{
    [Fact]
    public void Parse_TreatsEachNonBlankLineAsACueWithNoTiming()
    {
        var parser = new PlainTextTranscriptParser();

        var result = parser.Parse("First line\n\nSecond line\n");

        Assert.Equal("text", result.SourceFormat);
        Assert.Equal(2, result.Cues.Count);
        // Null, not 0. The old sentinel required every consumer to remember to reinterpret it,
        // and none did - 83% of an indexed corpus reported a confident 00:00 (#32).
        Assert.All(result.Cues, c =>
        {
            Assert.Null(c.StartSeconds);
            Assert.Null(c.EndSeconds);
        });
        Assert.Equal("First line", result.Cues[0].Text);
        Assert.Equal("Second line", result.Cues[1].Text);
    }
}
