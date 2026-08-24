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

    [Fact]
    public void FindMatches_CrossFileBoundaryCostsMoreThanWordBoundary()
    {
        // Milestone 4: "super" + cross-file + "man" should cost more to align against a plain
        // "superman" query than the same phonemes joined by an ordinary word boundary would.
        var query = Phonemes("s", "uː", "p", "ə", "m", "æ", "n");

        var withWordBoundary = new List<PhoneToken> { PhoneToken.Phoneme("s"), PhoneToken.Phoneme("uː"), PhoneToken.Phoneme("p"), PhoneToken.Phoneme("ə"), PhoneToken.Boundary, PhoneToken.Phoneme("m"), PhoneToken.Phoneme("æ"), PhoneToken.Phoneme("n") };
        var withCrossFileBoundary = new List<PhoneToken> { PhoneToken.Phoneme("s"), PhoneToken.Phoneme("uː"), PhoneToken.Phoneme("p"), PhoneToken.Phoneme("ə"), PhoneToken.CrossFileBoundary, PhoneToken.Phoneme("m"), PhoneToken.Phoneme("æ"), PhoneToken.Phoneme("n") };

        var wordBoundaryMatch = Assert.Single(PhoneticSequenceMatcher.FindMatches(query, withWordBoundary, DefaultOptions));
        var crossFileMatch = Assert.Single(PhoneticSequenceMatcher.FindMatches(query, withCrossFileBoundary, DefaultOptions));

        Assert.True(crossFileMatch.Cost > wordBoundaryMatch.Cost);
        Assert.Equal(DefaultOptions.CrossFileTransitionCost - DefaultOptions.WordBoundaryCost, crossFileMatch.Cost - wordBoundaryMatch.Cost, precision: 6);
    }

    [Fact]
    public void FindMatches_CorrespondencesMapQueryIndicesToCandidateIndices()
    {
        var query = Phonemes("s", "æ", "t");
        var candidate = Phonemes("s", "æ", "t");

        var match = Assert.Single(PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions));

        Assert.Equal([(0, 0), (1, 1), (2, 2)], match.Correspondences);
    }

    /// <summary>#15: an exact subsequence's alignment steps must all be Match, not Substitute - the DP's own Move.Substitute covers both, so the conversion has to tell them apart by symbol equality.</summary>
    [Fact]
    public void FindMatches_ExactSubsequence_AlignmentStepsAreAllMatch()
    {
        var query = Phonemes("s", "æ", "t");
        var candidate = Phonemes("s", "æ", "t");

        var match = Assert.Single(PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions));

        Assert.Equal(3, match.AlignmentSteps.Count);
        Assert.All(match.AlignmentSteps, s => Assert.Equal(AlignmentOp.Match, s.Op));
        Assert.Equal([(0, 0), (1, 1), (2, 2)], match.AlignmentSteps.Select(s => (s.QueryIndex, s.CandidateIndex)));
    }

    [Fact]
    public void FindMatches_CloseSubstitution_AlignmentStepIsSubstituteAtTheRightIndex()
    {
        var candidate = Phonemes("ə", "s");
        var query = Phonemes("ə", "z");

        var match = Assert.Single(PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions));

        Assert.Equal(AlignmentOp.Match, match.AlignmentSteps[0].Op);
        Assert.Equal(AlignmentOp.Substitute, match.AlignmentSteps[1].Op);
        Assert.Equal((1, 1), (match.AlignmentSteps[1].QueryIndex, match.AlignmentSteps[1].CandidateIndex));
    }

    /// <summary>Query has a phoneme the candidate doesn't - QueryExtra, with no CandidateIndex.</summary>
    [Fact]
    public void FindMatches_QueryLongerThanCandidate_ProducesAQueryExtraStep()
    {
        var candidate = Phonemes("k", "æ");
        var query = Phonemes("k", "æ", "t");

        var match = Assert.Single(PhoneticSequenceMatcher.FindMatches(query, candidate, DefaultOptions));

        var extra = Assert.Single(match.AlignmentSteps, s => s.Op == AlignmentOp.QueryExtra);
        Assert.Equal(2, extra.QueryIndex);
        Assert.Null(extra.CandidateIndex);
    }

    /// <summary>
    /// Candidate has a phoneme the query doesn't - CandidateExtra, with no QueryIndex. The extra
    /// phoneme is placed in the middle, not at the match's start: free-start alignment lets the
    /// query begin at any candidate column for free, so an extra phoneme at the very start is
    /// ambiguous with "skip it as an unmatched prefix and substitute the query's first phoneme
    /// against whatever comes after it instead" - which is what a first version of this test
    /// actually found the DP preferring, once it happened to be cheaper than paying the deletion
    /// cost outright.
    /// </summary>
    [Fact]
    public void FindMatches_CandidateHasAnExtraPhoneme_ProducesACandidateExtraStep()
    {
        var candidate = Phonemes("k", "æ", "r", "t");
        var query = Phonemes("k", "æ", "t");

        // Deletion (candidate-extra) made cheap relative to substitution, so the DP can't prefer
        // substituting the extra phoneme against a query phoneme instead of just dropping it -
        // otherwise which one wins depends on how phonetically close "r" happens to be scored
        // against the query's phonemes, which a first version of this test learned the hard way.
        var options = DefaultOptions with { InsertionCost = 0.05 };
        var match = Assert.Single(PhoneticSequenceMatcher.FindMatches(query, candidate, options));

        var extra = Assert.Single(match.AlignmentSteps, s => s.Op == AlignmentOp.CandidateExtra);
        Assert.Equal(2, extra.CandidateIndex);
        Assert.Null(extra.QueryIndex);
    }
}
