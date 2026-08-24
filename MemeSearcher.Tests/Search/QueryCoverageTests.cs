using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

/// <summary>
/// #25: QueryCoverage is the single place raw (boundary-included) query-token indices get mapped
/// onto the boundary-filtered QueryPhonemes space every result surfaces - and the single place a
/// match's covered-query envelope gets computed. Both PhoneticSearchService and
/// CompositeSearchService go through this now; a bug here would silently misplace every coverage
/// strip and every composite component span.
/// </summary>
public class QueryCoverageTests
{
    private static PhoneToken Word(string symbol) => PhoneToken.Phoneme(symbol);

    [Fact]
    public void BuildIndexMap_SingleWordQuery_IsTheIdentityMapping()
    {
        var tokens = new[] { Word("s"), Word("uː"), Word("p") };

        var map = QueryCoverage.BuildIndexMap(tokens);

        Assert.Equal([0, 1, 2], map);
    }

    [Fact]
    public void BuildIndexMap_MultiWordQuery_SkipsBoundaryTokensAndShiftsWhatFollows()
    {
        // "super man": 3 phones, a boundary, then 2 phones.
        var tokens = new[] { Word("s"), Word("uː"), Word("p"), PhoneToken.Boundary, Word("m"), Word("æ") };

        var map = QueryCoverage.BuildIndexMap(tokens);

        Assert.Equal([0, 1, 2, -1, 3, 4], map);
    }

    [Fact]
    public void ComputeSpan_ContiguousIndices_ReturnsTheirEnvelope()
    {
        var (start, end) = QueryCoverage.ComputeSpan([2, 3, 4]);

        Assert.Equal(2, start);
        Assert.Equal(5, end);
    }

    [Fact]
    public void ComputeSpan_OutOfOrderIndices_StillReturnsMinToMaxPlusOne()
    {
        var (start, end) = QueryCoverage.ComputeSpan([4, 1, 3]);

        Assert.Equal(1, start);
        Assert.Equal(5, end);
    }

    [Fact]
    public void ComputeSpan_NoIndices_ReturnsAnEmptyZeroSpan()
    {
        var (start, end) = QueryCoverage.ComputeSpan([]);

        Assert.Equal(0, start);
        Assert.Equal(0, end);
    }
}
