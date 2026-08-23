using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.Tests.Transcription;

public class VttTranscriptParserTests
{
    private const string Sample = """
        WEBVTT

        00:00:01.000 --> 00:00:04.500 align:start position:10%
        Hello world

        cue-2
        00:01:05.000 --> 00:01:07.250
        Minutes-only timestamp

        """;

    [Fact]
    public void Parse_ExtractsCuesIgnoringHeaderAndCueSettings()
    {
        var parser = new VttTranscriptParser();

        var result = parser.Parse(Sample);

        Assert.Equal("vtt", result.SourceFormat);
        Assert.Equal(2, result.Cues.Count);

        Assert.Equal(1.0, result.Cues[0].StartSeconds!.Value, 3);
        Assert.Equal(4.5, result.Cues[0].EndSeconds!.Value, 3);
        Assert.Equal("Hello world", result.Cues[0].Text);

        Assert.Equal(65.0, result.Cues[1].StartSeconds!.Value, 3);
        Assert.Equal(67.25, result.Cues[1].EndSeconds!.Value, 3);
        Assert.Equal("Minutes-only timestamp", result.Cues[1].Text);
    }
}
