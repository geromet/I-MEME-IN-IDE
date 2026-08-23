using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

public class PhoneStreamBuilderTests
{
    private static Word MakeWord(Guid segmentId, int sequence, string text, string phonemeSequence, double start, double end) => new()
    {
        Id = Guid.NewGuid(),
        SegmentId = segmentId,
        Sequence = sequence,
        Text = text,
        PhonemeSequence = phonemeSequence,
        StartSeconds = start,
        EndSeconds = end,
    };

    [Fact]
    public void Build_InsertsBoundaryBetweenWordsWithinASegment()
    {
        var segmentId = Guid.NewGuid();
        var among = MakeWord(segmentId, 0, "among", "ɐ m ʌ ŋ", 1.0, 1.5);
        var us = MakeWord(segmentId, 1, "us", "ʌ s", 1.5, 2.0);

        var segment = new Segment
        {
            Id = segmentId,
            TranscriptId = Guid.NewGuid(),
            Sequence = 0,
            Text = "among us",
            StartSeconds = 1.0,
            EndSeconds = 2.0,
            Words = [among, us],
        };

        var transcript = new Transcript
        {
            Id = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            Source = "srt",
            Language = "en-US",
            Segments = [segment],
        };

        var stream = PhoneStreamBuilder.Build(transcript);

        Assert.Equal(
            ["ɐ", "m", "ʌ", "ŋ", "|", "ʌ", "s"],
            stream.Select(e => e.Token.IsBoundary ? "|" : e.Token.Symbol));

        // Boundary entries carry no provenance.
        var boundary = stream.Single(e => e.Token.IsBoundary);
        Assert.Null(boundary.WordId);
        Assert.Null(boundary.StartSeconds);
    }

    [Fact]
    public void Build_InsertsBoundaryAcrossSegments()
    {
        var segment1Id = Guid.NewGuid();
        var segment2Id = Guid.NewGuid();

        var segment1 = new Segment
        {
            Id = segment1Id,
            TranscriptId = Guid.NewGuid(),
            Sequence = 0,
            Text = "hello",
            StartSeconds = 0,
            EndSeconds = 1,
            Words = [MakeWord(segment1Id, 0, "hello", "h ə l oʊ", 0, 1)],
        };

        var segment2 = new Segment
        {
            Id = segment2Id,
            TranscriptId = Guid.NewGuid(),
            Sequence = 1,
            Text = "world",
            StartSeconds = 2,
            EndSeconds = 3,
            Words = [MakeWord(segment2Id, 0, "world", "w ɜː l d", 2, 3)],
        };

        var transcript = new Transcript
        {
            Id = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            Source = "srt",
            Language = "en-US",
            Segments = [segment1, segment2],
        };

        var stream = PhoneStreamBuilder.Build(transcript);

        Assert.Contains(stream, e => e.Token.IsBoundary);
        // The stream is continuous across the segment boundary - not two separate lists.
        Assert.Equal(1 + 4 + 4, stream.Count); // "hello"(4) + boundary(1) + "world"(4)
    }

    [Fact]
    public void Build_SkipsWordsWithoutPhonemeData()
    {
        var segmentId = Guid.NewGuid();
        var withPhonemes = MakeWord(segmentId, 0, "hi", "h aɪ", 0, 1);
        var withoutPhonemes = new Word
        {
            Id = Guid.NewGuid(),
            SegmentId = segmentId,
            Sequence = 1,
            Text = "there",
            PhonemeSequence = null, // not yet phonemized (addendum §28: valid partial state)
            StartSeconds = 1,
            EndSeconds = 2,
        };

        var segment = new Segment
        {
            Id = segmentId,
            TranscriptId = Guid.NewGuid(),
            Sequence = 0,
            Text = "hi there",
            StartSeconds = 0,
            EndSeconds = 2,
            Words = [withPhonemes, withoutPhonemes],
        };

        var transcript = new Transcript
        {
            Id = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            Source = "srt",
            Language = "en-US",
            Segments = [segment],
        };

        var stream = PhoneStreamBuilder.Build(transcript);

        Assert.Equal(["h", "aɪ"], stream.Select(e => e.Token.Symbol));
    }
}
