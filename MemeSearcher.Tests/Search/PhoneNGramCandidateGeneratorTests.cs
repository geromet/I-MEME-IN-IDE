using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

public class PhoneNGramCandidateGeneratorTests
{
    [Fact]
    public void GenerateWindows_EmptyNGramsReturnsNull()
    {
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            [], _ => [], candidateLength: 100, padding: 5);

        Assert.Null(windows);
    }

    [Fact]
    public void GenerateWindows_NoHitsReturnsEmptyList()
    {
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            ["xyz"], _ => [], candidateLength: 100, padding: 5);

        Assert.NotNull(windows);
        Assert.Empty(windows);
    }

    [Fact]
    public void GenerateWindows_PadsAroundEachHit()
    {
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            ["abc"], ngram => ngram == "abc" ? [50] : [], candidateLength: 100, padding: 5);

        var window = Assert.Single(windows!);
        Assert.Equal(45, window.Start);
        Assert.Equal(56, window.End); // exclusive, hit + padding + 1
    }

    [Fact]
    public void GenerateWindows_ClampsToCandidateBounds()
    {
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            ["abc"], _ => [2], candidateLength: 10, padding: 5);

        var window = Assert.Single(windows!);
        Assert.Equal(0, window.Start); // hit - padding would be negative
        Assert.Equal(8, window.End); // hit + padding + 1
    }

    [Fact]
    public void GenerateWindows_MergesOverlappingHits()
    {
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            ["a", "b"], ngram => ngram == "a" ? [10] : [15], candidateLength: 100, padding: 5);

        // [5,16) and [10,21) overlap - one merged window, not two.
        var window = Assert.Single(windows!);
        Assert.Equal(5, window.Start);
        Assert.Equal(21, window.End);
    }

    [Fact]
    public void GenerateWindows_KeepsFarApartHitsSeparate()
    {
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            ["a", "b"], ngram => ngram == "a" ? [10] : [80], candidateLength: 100, padding: 5);

        Assert.Equal(2, windows!.Count);
    }

    private static readonly PhoneticSearchOptions SimilarOptions = PhoneticSearchOptions.ForMode(SearchMode.SimilarPhonetic);
    private static readonly PhoneticSearchOptions ExactOptions = PhoneticSearchOptions.ForMode(SearchMode.ExactPhonetic);

    [Fact]
    public void ExpandFuzzy_AlwaysIncludesTheExactNGrams()
    {
        var expanded = PhoneNGramCandidateGenerator.ExpandFuzzy(
            [PhoneNGramIndexer.Join(["p", "æ", "t"])], SimilarOptions, queryLength: 10);

        Assert.Contains(PhoneNGramIndexer.Join(["p", "æ", "t"]), expanded);
    }

    [Fact]
    public void ExpandFuzzy_AddsANearbyOneSymbolVariant()
    {
        // "p" (voiceless bilabial stop) and "b" (voiced bilabial stop) differ by voicing alone -
        // one of the closest possible substitutions in the feature table.
        var exact = PhoneNGramIndexer.Join(["p", "æ", "t"]);

        var expanded = PhoneNGramCandidateGenerator.ExpandFuzzy([exact], SimilarOptions, queryLength: 10);

        Assert.Contains(PhoneNGramIndexer.Join(["b", "æ", "t"]), expanded);
    }

    [Fact]
    public void ExpandFuzzy_AddsACrossClassVariantWhenTheQueryHasBudgetForOne()
    {
        // A cross-class (vowel/consonant) substitution costs more than SubstitutionMaxCost itself
        // (CrossClassMultiplier), but a long-enough query's *overall* accepted-cost budget can
        // still afford spending most of it on a single such substitution - expansion must not
        // categorically rule these out, or a real match relying on one is unreachable (#9).
        var exact = PhoneNGramIndexer.Join(["p", "æ", "t"]);

        var expanded = PhoneNGramCandidateGenerator.ExpandFuzzy([exact], SimilarOptions, queryLength: 20);

        Assert.Contains(exact.Replace('æ', 's'), expanded); // vowel -> consonant, cross-class
    }

    [Fact]
    public void ExpandFuzzy_ZeroBudgetAddsNothing()
    {
        // MinimumScore = 1.0 with a short query leaves no room for any substitution at all.
        var exact = PhoneNGramIndexer.Join(["p", "æ", "t"]);

        var expanded = PhoneNGramCandidateGenerator.ExpandFuzzy(
            [exact], SimilarOptions with { MinimumScore = 1.0 }, queryLength: 3);

        Assert.Equal([exact], expanded);
    }

    [Fact]
    public void ExpandFuzzy_InfiniteCostAddsNothing()
    {
        // ExactPhonetic mode: no substitution is ever cheap enough, so there is nothing fuzzy about
        // an "exact" search - expansion must be a no-op, not a lookup against an unusable threshold.
        var exact = PhoneNGramIndexer.Join(["p", "æ", "t"]);

        var expanded = PhoneNGramCandidateGenerator.ExpandFuzzy([exact], ExactOptions, queryLength: 10);

        Assert.Equal([exact], expanded);
    }
}
