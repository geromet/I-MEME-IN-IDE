using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

public class PhoneNGramIndexerTests
{
    private static List<PhoneToken> Phonemes(params string[] symbols) =>
        symbols.Select(PhoneToken.Phoneme).ToList();

    [Fact]
    public void Extract_ReturnsOneTrigramPerConsecutiveWindow()
    {
        var tokens = Phonemes("a", "b", "c", "d");

        var occurrences = PhoneNGramIndexer.Extract(tokens);

        Assert.Equal(2, occurrences.Count);
        Assert.Equal(PhoneNGramIndexer.Join(["a", "b", "c"]), occurrences[0].NGram);
        Assert.Equal(0, occurrences[0].Position);
        Assert.Equal(PhoneNGramIndexer.Join(["b", "c", "d"]), occurrences[1].NGram);
        Assert.Equal(1, occurrences[1].Position);
    }

    [Fact]
    public void Extract_FewerThanThreePhonemesProducesNoOccurrences()
    {
        var occurrences = PhoneNGramIndexer.Extract(Phonemes("a", "b"));

        Assert.Empty(occurrences);
    }

    [Fact]
    public void Extract_TrigramsSpanWordBoundaries()
    {
        // "a b | c" (word boundary after b) still has a trigram covering a-b-c: boundaries carry
        // no symbol and must not break a trigram that spans them (#9).
        var tokens = new List<PhoneToken>
        {
            PhoneToken.Phoneme("a"),
            PhoneToken.Phoneme("b"),
            PhoneToken.Boundary,
            PhoneToken.Phoneme("c"),
        };

        var occurrences = PhoneNGramIndexer.Extract(tokens);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal(PhoneNGramIndexer.Join(["a", "b", "c"]), occurrence.NGram);

        // The position is the *token-list* index of the trigram's first phoneme (0), not its index
        // in a hypothetical boundary-free sequence - this is the coordinate space
        // PhoneticSequenceMatcher's Start/End already use.
        Assert.Equal(0, occurrence.Position);
    }

    [Fact]
    public void Extract_PositionsAreIndicesIntoTheOriginalTokenList()
    {
        var tokens = new List<PhoneToken>
        {
            PhoneToken.Boundary,
            PhoneToken.Phoneme("a"),
            PhoneToken.Phoneme("b"),
            PhoneToken.Phoneme("c"),
        };

        var occurrence = Assert.Single(PhoneNGramIndexer.Extract(tokens));

        Assert.Equal(1, occurrence.Position);
        Assert.Equal("a", tokens[occurrence.Position].Symbol);
    }

    [Fact]
    public void JoinThenSplit_RoundTrips()
    {
        var symbols = new[] { "tʃ", "ɹ", "ə" };

        var roundTripped = PhoneNGramIndexer.Split(PhoneNGramIndexer.Join(symbols));

        Assert.Equal(symbols, roundTripped);
    }

    [Fact]
    public void Join_DifferentSymbolsNeverProduceTheSameKey()
    {
        // A naive concatenation (no separator) would collide here: "a"+"bc" == "ab"+"c".
        var keyOne = PhoneNGramIndexer.Join(["a", "bc"]);
        var keyTwo = PhoneNGramIndexer.Join(["ab", "c"]);

        Assert.NotEqual(keyOne, keyTwo);
    }
}
