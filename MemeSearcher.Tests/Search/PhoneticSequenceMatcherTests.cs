using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

public class PhoneticSequenceMatcherTests
{
    private static readonly PhoneticSearchOptions DefaultOptions = PhoneticSearchOptions.ForMode(SearchMode.SimilarPhonetic);

    private static List<PhoneToken> Phonemes(params string[] symbols) =>
        symbols.Select(PhoneToken.Phoneme).ToList();

    private static List<PhoneToken> Words(params string[][] words)
    {
        var tokens = new List<PhoneToken>();
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
            {
                tokens.Add(PhoneToken.Boundary);
            }

            tokens.AddRange(words[i].Select(PhoneToken.Phoneme));
        }

        return tokens;
    }

    [Fact]
    public void FindMatches_ExactSubsequenceHasZeroCost()
    {
        var candidate = Phonemes("a", "b", "c", "d", "e");
        var query = Phonemes("b", "c", "d");

        var matches = PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions);

        var match = Assert.Single(matches);
        Assert.Equal(0, match.Cost);
        Assert.Equal(1, match.Start);
        Assert.Equal(4, match.End);
    }

    [Fact]
    public void FindMatches_ToleratesACloseSubstitution()
    {
        // Candidate says "s" where the query says "z" - a voicing-only difference should still match.
        var candidate = Phonemes("ə", "s");
        var query = Phonemes("ə", "z");

        var matches = PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions);

        var match = Assert.Single(matches);
        Assert.True(match.Cost > 0);
        Assert.True(match.Cost < 0.3);
    }

    [Fact]
    public void FindMatches_CompletelyUnrelatedSequenceScoresBelowThreshold()
    {
        var candidate = Phonemes("m", "n", "ŋ");
        var query = Phonemes("æ", "iː", "uː");

        var matches = PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_CrossesWordBoundaryAtADifferentPositionThanTheQuery()
    {
        // "ice cream" (candidate: "aɪ s" | "k ɹ iː m") vs a differently-segmented candidate where
        // the boundary falls in a different place: "aɪ" | "s k ɹ iː m" (as if spoken "I scream").
        // The phoneme content is identical either way - only the boundary position differs -
        // so this must score as a near-exact match despite the word split moving.
        var candidateIceCream = Words(["aɪ", "s"], ["k", "ɹ", "iː", "m"]);
        var candidateIScream = Words(["aɪ"], ["s", "k", "ɹ", "iː", "m"]);
        var query = Words(["aɪ", "s"], ["k", "ɹ", "iː", "m"]);

        var matchesSameSplit = PhoneticSequenceMatcher.FindMatches(query, candidateIceCream, DefaultOptions);
        var matchesDifferentSplit = PhoneticSequenceMatcher.FindMatches(query, candidateIScream, DefaultOptions);

        Assert.Single(matchesSameSplit);
        Assert.Equal(0, matchesSameSplit[0].Cost);

        Assert.Single(matchesDifferentSplit);
        Assert.True(matchesDifferentSplit[0].Cost < DefaultOptions.WordBoundaryCost * 4,
            $"expected a cheap boundary-shift cost, got {matchesDifferentSplit[0].Cost}");
    }

    [Fact]
    public void FindMatches_QueryCanStartAndEndMidStream()
    {
        // handoff §16: "to the store" should be found inside a longer continuous stream without
        // requiring the match to align to whole segment/word-group boundaries in the setup here.
        var candidate = Words(["aɪ"], ["w", "ɛ", "n", "t"], ["t", "ə"], ["ð", "ə"], ["s", "t", "ɔː"]);
        var query = Words(["t", "ə"], ["ð", "ə"], ["s", "t", "ɔː"]);

        var matches = PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions);

        Assert.Contains(matches, m => m.Cost == 0);
    }

    [Fact]
    public void FindMatches_EmptyQueryOrCandidateReturnsNoMatches()
    {
        Assert.Empty(PhoneticSequenceMatcher.FindMatches([], Phonemes("a"), DefaultOptions));
        Assert.Empty(PhoneticSequenceMatcher.FindMatches(Phonemes("a"), [], DefaultOptions));
    }

    [Fact]
    public void FindMatches_ExactModeRejectsAnySubstitution()
    {
        var options = PhoneticSearchOptions.ForMode(SearchMode.ExactPhonetic);
        var candidate = Phonemes("s", "æ", "t"); // "sat"
        var query = Phonemes("z", "æ", "t");     // "zat" - one substitution away

        var matches = PhoneticSequenceMatcher.FindMatches(query, candidate, options);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_ExactModeAcceptsALiteralSubsequence()
    {
        var options = PhoneticSearchOptions.ForMode(SearchMode.ExactPhonetic);
        var candidate = Phonemes("s", "æ", "t");
        var query = Phonemes("s", "æ", "t");

        var matches = PhoneticSequenceMatcher.FindMatches(query, candidate, options);

        var match = Assert.Single(matches);
        Assert.Equal(0, match.Cost);
    }
}
