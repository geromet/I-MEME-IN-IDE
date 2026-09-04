using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

public class MediaSearchFacetsTests
{
    private static Media CreateMedia(
        string? channel = "Example Channel",
        string language = "en-US",
        YtDlpMediaKind? mediaKind = YtDlpMediaKind.Video,
        DateOnly? uploadDate = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Path = "/tmp/example.srt",
            Language = language,
            ContentHash = "hash",
            Channel = channel,
            YtDlpMediaKind = mediaKind,
            UploadDate = uploadDate ?? new DateOnly(2026, 1, 15),
        };

    [Fact]
    public void Empty_MatchesEveryMediaItem()
    {
        Assert.True(MediaSearchFacets.Empty.Matches(CreateMedia()));
        Assert.True(MediaSearchFacets.Empty.Matches(CreateMedia(channel: null, mediaKind: null)));
    }

    [Fact]
    public void ChannelAndLanguageMatching_IsCaseInsensitiveAndIntersected()
    {
        var facets = new MediaSearchFacets
        {
            Channels = ["example channel"],
            Languages = ["EN-us"],
        };

        Assert.True(facets.Matches(CreateMedia()));
        Assert.False(facets.Matches(CreateMedia(channel: "Other Channel")));
        Assert.False(facets.Matches(CreateMedia(language: "nl-NL")));
    }

    [Fact]
    public void UnknownChannel_IsExplicitlyOptIn()
    {
        var unknown = CreateMedia(channel: null);

        Assert.False(new MediaSearchFacets { Channels = ["Example Channel"] }.Matches(unknown));
        Assert.True(new MediaSearchFacets { IncludeUnknownChannel = true }.Matches(unknown));
    }

    [Fact]
    public void NonYtDlpMedia_IsExplicitlyOptIn()
    {
        var localImport = CreateMedia(mediaKind: null);

        Assert.False(new MediaSearchFacets { MediaKinds = [YtDlpMediaKind.Video] }.Matches(localImport));
        Assert.True(new MediaSearchFacets { IncludeNonYtDlpMedia = true }.Matches(localImport));
    }

    [Fact]
    public void UploadDateBounds_AreInclusiveAndExcludeUnknownDates()
    {
        var facets = new MediaSearchFacets
        {
            UploadedOnOrAfter = new DateOnly(2026, 1, 15),
            UploadedOnOrBefore = new DateOnly(2026, 1, 20),
        };

        Assert.True(facets.Matches(CreateMedia(uploadDate: new DateOnly(2026, 1, 15))));
        Assert.True(facets.Matches(CreateMedia(uploadDate: new DateOnly(2026, 1, 20))));
        Assert.False(facets.Matches(CreateMedia(uploadDate: new DateOnly(2026, 1, 14))));

        var unknown = CreateMedia();
        unknown.UploadDate = null;
        Assert.False(facets.Matches(unknown));
    }

    [Fact]
    public void MediaKindFilter_AcceptsOnlySelectedKinds()
    {
        var facets = new MediaSearchFacets { MediaKinds = [YtDlpMediaKind.Audio] };

        Assert.True(facets.Matches(CreateMedia(mediaKind: YtDlpMediaKind.Audio)));
        Assert.False(facets.Matches(CreateMedia(mediaKind: YtDlpMediaKind.Video)));
    }
}
