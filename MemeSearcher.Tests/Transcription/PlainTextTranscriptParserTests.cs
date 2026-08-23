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
        Assert.All(result.Cues, c =>
        {
            Assert.Equal(0, c.StartSeconds);
            Assert.Equal(0, c.EndSeconds);
        });
        Assert.Equal("First line", result.Cues[0].Text);
        Assert.Equal("Second line", result.Cues[1].Text);
    }
}
