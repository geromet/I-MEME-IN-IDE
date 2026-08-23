using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.Tests.Transcription;

public class SrtTranscriptParserTests
{
    private const string Sample = """
        1
        00:00:01,000 --> 00:00:04,500
        Hello world

        2
        00:00:05,000 --> 00:00:07,250
        Second line
        wrapped onto two rows

        """;

    [Fact]
    public void Parse_ExtractsCuesWithTimestampsAndText()
    {
        var parser = new SrtTranscriptParser();

        var result = parser.Parse(Sample);

        Assert.Equal("srt", result.SourceFormat);
        Assert.Equal(2, result.Cues.Count);

        Assert.Equal(1.0, result.Cues[0].StartSeconds, 3);
        Assert.Equal(4.5, result.Cues[0].EndSeconds, 3);
        Assert.Equal("Hello world", result.Cues[0].Text);

        Assert.Equal(5.0, result.Cues[1].StartSeconds, 3);
        Assert.Equal(7.25, result.Cues[1].EndSeconds, 3);
        Assert.Equal("Second line wrapped onto two rows", result.Cues[1].Text);
    }

    [Theory]
    [InlineData("transcript.srt", true)]
    [InlineData("transcript.SRT", true)]
    [InlineData("transcript.vtt", false)]
    public void CanParse_MatchesByExtension(string path, bool expected)
    {
        Assert.Equal(expected, new SrtTranscriptParser().CanParse(path));
    }
}
