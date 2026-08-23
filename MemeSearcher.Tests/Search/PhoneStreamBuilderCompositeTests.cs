using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

public class PhoneStreamBuilderCompositeTests
{
    private static Transcript MakeTranscript(Guid mediaId, string word, string phonemeSequence)
    {
        var segmentId = Guid.NewGuid();
        return new Transcript
        {
            Id = Guid.NewGuid(),
            MediaId = mediaId,
            Source = "srt",
            Language = "en-US",
            Segments =
            [
                new Segment
                {
                    Id = segmentId,
                    TranscriptId = Guid.NewGuid(),
                    Sequence = 0,
                    Text = word,
                    StartSeconds = 0,
                    EndSeconds = 1,
                    Words =
                    [
                        new Word
                        {
                            Id = Guid.NewGuid(),
                            SegmentId = segmentId,
                            Sequence = 0,
                            Text = word,
                            PhonemeSequence = phonemeSequence,
                            StartSeconds = 0,
                            EndSeconds = 1,
                        },
                    ],
                },
            ],
        };
    }

    [Fact]
    public void BuildComposite_InsertsCrossFileBoundaryBetweenDifferentMedia()
    {
        var mediaA = Guid.NewGuid();
        var mediaB = Guid.NewGuid();
        var transcriptA = MakeTranscript(mediaA, "super", "s uː p ə");
        var transcriptB = MakeTranscript(mediaB, "man", "m æ n");

        var stream = PhoneStreamBuilder.BuildComposite([[transcriptA], [transcriptB]]);

        var boundaryEntry = stream.Single(e => e.Token.IsBoundary);
        Assert.True(boundaryEntry.Token.IsCrossFileBoundary);
        Assert.Null(boundaryEntry.MediaId);

        var phonemeEntries = stream.Where(e => !e.Token.IsBoundary).ToList();
        Assert.All(phonemeEntries.Take(4), e => Assert.Equal(mediaA, e.MediaId));
        Assert.All(phonemeEntries.Skip(4), e => Assert.Equal(mediaB, e.MediaId));
    }

    [Fact]
    public void BuildComposite_MultipleTranscriptsInOneGroupUseAnOrdinaryBoundary()
    {
        // Real callers (CompositeSearchService) group all of one media's transcripts into a
        // single element of the outer sequence (addendum §5: a media item can have more than one
        // transcript) - only a transition between groups is a cross-file boundary.
        var mediaId = Guid.NewGuid();
        var first = MakeTranscript(mediaId, "hello", "h ə l oʊ");
        var second = MakeTranscript(mediaId, "world", "w ɜː l d");

        var stream = PhoneStreamBuilder.BuildComposite([[first, second]]);

        var boundaryEntry = stream.Single(e => e.Token.IsBoundary);
        Assert.False(boundaryEntry.Token.IsCrossFileBoundary);
        Assert.All(stream.Where(e => !e.Token.IsBoundary), e => Assert.Equal(mediaId, e.MediaId));
    }
}
