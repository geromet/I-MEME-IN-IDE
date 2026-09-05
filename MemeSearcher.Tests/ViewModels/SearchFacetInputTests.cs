using MemeSearcher.Core.Models;
using MemeSearcher.ViewModels;

namespace MemeSearcher.Tests.ViewModels;

public class SearchFacetInputTests
{
    [Fact]
    public void TryBuild_NormalizesCompactInputsIntoExistingFacetContract()
    {
        var input = new SearchFacetInput(
            "Channel A, channel b;CHANNEL A",
            "en-US; nl-NL",
            IncludeUnknownChannel: true,
            IncludeAudio: true,
            IncludeVideo: false,
            IncludeLocalMedia: true,
            UploadedOnOrAfterText: "2025-01-02",
            UploadedOnOrBeforeText: "2025-12-31");

        Assert.True(input.TryBuild(out var facets, out var error));
        Assert.Null(error);
        Assert.Equal(2, facets.Channels.Count);
        Assert.Contains(facets.Channels, channel => channel == "Channel A");
        Assert.Contains(facets.Channels, channel => channel == "channel b");
        Assert.True(facets.IncludeUnknownChannel);
        Assert.Equal(["en-US", "nl-NL"], facets.Languages);
        Assert.Equal([YtDlpMediaKind.Audio], facets.MediaKinds);
        Assert.True(facets.IncludeNonYtDlpMedia);
        Assert.Equal(new DateOnly(2025, 1, 2), facets.UploadedOnOrAfter);
        Assert.Equal(new DateOnly(2025, 12, 31), facets.UploadedOnOrBefore);
        Assert.False(facets.IsEmpty);
    }

    [Theory]
    [InlineData("2025/01/02", "", "Upload date from must use YYYY-MM-DD.")]
    [InlineData("", "02-01-2025", "Upload date to must use YYYY-MM-DD.")]
    [InlineData("2025-12-31", "2025-01-01", "Upload date from must not be after upload date to.")]
    public void TryBuild_RejectsInvalidOrReversedDateBounds(string from, string to, string expectedError)
    {
        var input = new SearchFacetInput("", "", false, false, false, false, from, to);

        Assert.False(input.TryBuild(out var facets, out var error));
        Assert.Same(MemeSearcher.Core.Search.MediaSearchFacets.Empty, facets);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void TryBuild_EmptyControlsProduceEmptyFacets()
    {
        var input = new SearchFacetInput("", "", false, false, false, false, "", "");

        Assert.True(input.TryBuild(out var facets, out var error));
        Assert.Null(error);
        Assert.True(facets.IsEmpty);
    }
}
