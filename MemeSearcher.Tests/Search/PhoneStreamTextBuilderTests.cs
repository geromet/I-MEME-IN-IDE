using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

/// <summary>
/// #17: the canonical stream-to-text helper both PhoneticSearchService and CompositeSearchService
/// now call, replacing their own near-identical (and slightly drifted) private copies. Output must
/// match what each service already produced - "concat within a word, space between words" - since
/// this extraction is meant to be behavior-preserving.
/// </summary>
public class PhoneStreamTextBuilderTests
{
    private static PhoneStreamEntry Phone(string symbol, Guid wordId, string wordText) =>
        PhoneStreamEntry.Phoneme(symbol, Guid.NewGuid(), Guid.NewGuid(), wordId, wordText, null, null, false);

    [Fact]
    public void DistinctConsecutiveWords_CollapsesMultiplePhonesOfTheSameWordIntoOneEntry()
    {
        var wordId = Guid.NewGuid();
        var entries = new[] { Phone("l", wordId, "long"), Phone("ɔ", wordId, "long"), Phone("ŋ", wordId, "long") };

        Assert.Equal(["long"], PhoneStreamTextBuilder.DistinctConsecutiveWords(entries));
    }

    [Fact]
    public void DistinctConsecutiveWords_AcrossMultipleWords_YieldsOnePerWordInOrder()
    {
        var wordA = Guid.NewGuid();
        var wordB = Guid.NewGuid();
        var entries = new[]
        {
            Phone("ə", wordA, "a"),
            Phone("l", wordB, "long"), Phone("ɔ", wordB, "long"), Phone("ŋ", wordB, "long"),
        };

        Assert.Equal(["a", "long"], PhoneStreamTextBuilder.DistinctConsecutiveWords(entries));
    }

    [Fact]
    public void GroupByWord_ConcatenatesEachWordsPhonesIntoOneStringPerWord()
    {
        var wordA = Guid.NewGuid();
        var wordB = Guid.NewGuid();
        var entries = new[]
        {
            Phone("ə", wordA, "a"),
            Phone("l", wordB, "long"), Phone("ɔ", wordB, "long"), Phone("ŋ", wordB, "long"),
        };

        Assert.Equal(["ə", "lɔŋ"], PhoneStreamTextBuilder.GroupByWord(entries));
    }

    [Fact]
    public void BuildSourceTextAndBuildIpa_MatchTheServicesPreviousOutputShape()
    {
        var wordA = Guid.NewGuid();
        var wordB = Guid.NewGuid();
        var entries = new[]
        {
            Phone("ə", wordA, "a"),
            Phone("l", wordB, "long"), Phone("ɔ", wordB, "long"), Phone("ŋ", wordB, "long"),
        };

        Assert.Equal("a long", PhoneStreamTextBuilder.BuildSourceText(entries));
        Assert.Equal("ə lɔŋ", PhoneStreamTextBuilder.BuildIpa(entries));
    }
}
