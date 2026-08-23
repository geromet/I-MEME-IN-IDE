using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Tests.Transcripts;

public class WordSequenceAlignerTests
{
    private static IReadOnlyList<WordCorrespondence> Align(string transcript, string aligned) =>
        WordSequenceAligner.Align(transcript.Split(' '), aligned.Split(' '));

    [Fact]
    public void Align_MatchesIdenticalSequencesPositionally()
    {
        var result = Align("hello world again", "hello world again");

        Assert.Equal([(0, 0), (1, 1), (2, 2)], result.Select(c => (c.TranscriptIndex, c.AlignedIndex)));
    }

    /// <summary>
    /// The #30 regression, reduced. The aligner dropped the first word, which under the old
    /// time-range bucketing shifted every subsequent word by one - while the count check still
    /// passed, because a shifted window has the same cardinality as the correct one.
    /// </summary>
    [Fact]
    public void Align_DoesNotShiftWhenTheAlignerDropsTheFirstWord()
    {
        var result = Align("goedemorgen het is vrijdag", "het is vrijdag");

        // "goedemorgen" has no counterpart and must stay unmatched rather than adopting "het".
        Assert.DoesNotContain(result, c => c.TranscriptIndex == 0);
        Assert.Equal([(1, 0), (2, 1), (3, 2)], result.Select(c => (c.TranscriptIndex, c.AlignedIndex)));
    }

    [Fact]
    public void Align_HandlesAWordDroppedInTheMiddle()
    {
        var result = Align("een twee drie vier", "een drie vier");

        Assert.Equal([(0, 0), (2, 1), (3, 2)], result.Select(c => (c.TranscriptIndex, c.AlignedIndex)));
    }

    [Fact]
    public void Align_HandlesAnExtraWordFromTheAligner()
    {
        var result = Align("een drie", "een twee drie");

        Assert.Equal([(0, 0), (1, 2)], result.Select(c => (c.TranscriptIndex, c.AlignedIndex)));
    }

    /// <summary>
    /// A pairing whose texts differ is exactly the case that must not be trusted - it is how a
    /// word ends up with its neighbour's timing.
    /// </summary>
    [Fact]
    public void Align_NeverPairsWordsThatDoNotMatch()
    {
        var result = Align("alpha bravo charlie", "alpha zulu charlie");

        Assert.Equal([(0, 0), (2, 2)], result.Select(c => (c.TranscriptIndex, c.AlignedIndex)));
    }

    [Fact]
    public void Align_IgnoresCaseAndPunctuation()
    {
        // The aligner is fed the transcript's own text, so differences are formatting, not spelling.
        var result = Align("Goedemorgen. Het", "goedemorgen het");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Align_ReturnsNothingForEmptyInput()
    {
        Assert.Empty(WordSequenceAligner.Align([], ["a"]));
        Assert.Empty(WordSequenceAligner.Align(["a"], []));
    }

    [Fact]
    public void Align_MatchesRepeatedWordsInOrderRatherThanCollapsingThem()
    {
        // "de ... de" is common in Dutch; the second occurrence must not be matched to the first.
        var result = Align("de wereld de zon", "de wereld de zon");

        Assert.Equal([(0, 0), (1, 1), (2, 2), (3, 3)], result.Select(c => (c.TranscriptIndex, c.AlignedIndex)));
    }

    [Fact]
    public void Align_IsMonotonic()
    {
        var result = Align("een twee drie vier vijf", "een drie vier zes vijf");

        Assert.Equal(result.Select(c => c.TranscriptIndex).Order(), result.Select(c => c.TranscriptIndex));
        Assert.Equal(result.Select(c => c.AlignedIndex).Order(), result.Select(c => c.AlignedIndex));
    }
}
