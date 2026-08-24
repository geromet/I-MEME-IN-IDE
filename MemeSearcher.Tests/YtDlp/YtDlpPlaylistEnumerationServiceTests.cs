using MemeSearcher.Infrastructure.YtDlp;

namespace MemeSearcher.Tests.YtDlp;

/// <summary>
/// Tests ParseEntries against real `yt-dlp --flat-playlist --dump-json` output, captured by hand
/// against a real YouTube channel URL and a real playlist URL (trimmed of thumbnail arrays and
/// other noise irrelevant to parsing) - not assumed shapes. Deliberately does not invoke yt-dlp or
/// touch the network: live YouTube enumeration is exercised by hand, not in the automated suite,
/// since YouTube's own bot detection and video availability would make CI runs flaky for reasons
/// that have nothing to do with this code.
/// </summary>
public class YtDlpPlaylistEnumerationServiceTests
{
    // Real output for a channel URL (https://www.youtube.com/@NASA/videos): note there is no
    // top-level "channel"/"uploader" key at all in this mode - only "playlist_channel".
    private const string ChannelUrlLine =
        """{"title": "MAX POWER at NASA's Kennedy Space Center | Nov. 7-8, 2026", "duration": 77, "id": "UbAsuvO-164", "url": "https://www.youtube.com/watch?v=UbAsuvO-164", "webpage_url": "https://www.youtube.com/watch?v=UbAsuvO-164", "playlist_channel": "NASA", "playlist_channel_id": "UCLA_DiR1FfKNvjuUpBHmylQ", "n_entries": 2}""";

    // Real output for a playlist URL (a Pittsburgh ML Summit playlist): here "channel"/"uploader"
    // ARE present at the top level, in addition to the "playlist_channel" variant.
    private const string PlaylistUrlLine =
        """{"title": "Welcome from Google Developers - Pittsburgh ML Summit ‘19", "duration": 997, "channel": "Google for Developers", "channel_id": "UC_x5XG1OV2P6uZZ5FSM9Ttw", "uploader": "Google for Developers", "id": "CvTApw9X8aA", "url": "https://www.youtube.com/watch?v=CvTApw9X8aA", "playlist_channel": "Google for Developers", "playlist_count": 13}""";

    [Fact]
    public void ParseEntries_ChannelUrlShape_ReadsIdTitleUrlAndChannelFromThePlaylistChannelField()
    {
        var entries = YtDlpPlaylistEnumerationService.ParseEntries(ChannelUrlLine);

        var entry = Assert.Single(entries);
        Assert.Equal("UbAsuvO-164", entry.VideoId);
        Assert.Equal("MAX POWER at NASA's Kennedy Space Center | Nov. 7-8, 2026", entry.Title);
        Assert.Equal("NASA", entry.Channel);
        Assert.Equal("https://www.youtube.com/watch?v=UbAsuvO-164", entry.Url);
    }

    [Fact]
    public void ParseEntries_PlaylistUrlShape_ReadsChannelEvenThoughTheFieldNameDiffers()
    {
        var entries = YtDlpPlaylistEnumerationService.ParseEntries(PlaylistUrlLine);

        var entry = Assert.Single(entries);
        Assert.Equal("CvTApw9X8aA", entry.VideoId);
        Assert.Equal("Google for Developers", entry.Channel);
    }

    [Fact]
    public void ParseEntries_MultipleLines_ReturnsOneEntryPerLineInOrder()
    {
        var stdout = ChannelUrlLine + "\n" + PlaylistUrlLine + "\n";

        var entries = YtDlpPlaylistEnumerationService.ParseEntries(stdout);

        Assert.Equal(2, entries.Count);
        Assert.Equal("UbAsuvO-164", entries[0].VideoId);
        Assert.Equal("CvTApw9X8aA", entries[1].VideoId);
    }

    [Fact]
    public void ParseEntries_BlankLinesAndMalformedJson_AreSkippedRatherThanFailingTheWholeBatch()
    {
        var stdout = ChannelUrlLine + "\n\n" + "not valid json at all" + "\n" + PlaylistUrlLine;

        var entries = YtDlpPlaylistEnumerationService.ParseEntries(stdout);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ParseEntries_MissingIdOrTitle_SkipsTheRow()
    {
        var missingId = """{"title": "No id here", "url": "https://www.youtube.com/watch?v=x"}""";
        var missingTitle = """{"id": "abc123", "url": "https://www.youtube.com/watch?v=abc123"}""";

        Assert.Empty(YtDlpPlaylistEnumerationService.ParseEntries(missingId));
        Assert.Empty(YtDlpPlaylistEnumerationService.ParseEntries(missingTitle));
    }
}
