using MemeSearcher.Infrastructure.YtDlp;

namespace MemeSearcher.Tests.YtDlp;

/// <summary>
/// Tests ParseResult against real `yt-dlp --print-json` output, captured by hand from a real
/// download (audio-extracted from https://www.youtube.com/watch?v=jNQXAC9IVRw), trimmed of the
/// large format/URL noise irrelevant to parsing. Deliberately does not invoke yt-dlp or the
/// network - live download is exercised by hand, not in the automated suite (same rationale as
/// YtDlpPlaylistEnumerationServiceTests).
/// </summary>
public class YtDlpDownloadProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ytdlp-download-test-{Guid.NewGuid():N}");

    // The "_filename"/"ext" fields say "jNQXAC9IVRw.webm" - the pre-postprocessing name. The real
    // audio-extraction run actually produced "jNQXAC9IVRw.mp3" on disk, which is exactly why
    // ParseResult globs the download directory for "<id>.*" instead of trusting either field.
    private const string RealPrintJsonOutput =
        """{"id": "jNQXAC9IVRw", "title": "Me at the zoo", "channel": "jawed", "uploader": "jawed", "upload_date": "20050424", "ext": "webm", "_filename": "jNQXAC9IVRw.webm"}""";

    [Fact]
    public void ParseResult_ReadsIdTitleChannelAndUploadDate()
    {
        File.WriteAllText(Path.Combine(EnsureDir(), "jNQXAC9IVRw.mp3"), "fake audio content");

        var result = YtDlpDownloadProvider.ParseResult(RealPrintJsonOutput, _dir);

        Assert.Equal("jNQXAC9IVRw", result.VideoId);
        Assert.Equal("Me at the zoo", result.Title);
        Assert.Equal("jawed", result.Channel);
        Assert.Equal(new DateOnly(2005, 4, 24), result.UploadDate);
    }

    [Fact]
    public void ParseResult_FindsTheActualFileRegardlessOfWhatTheJsonSaysTheExtensionIs()
    {
        // Postprocessing (audio extraction here) changes the extension after yt-dlp already wrote
        // its JSON - ParseResult must find the real file on disk, not reconstruct a name from
        // "ext"/"_filename".
        File.WriteAllText(Path.Combine(EnsureDir(), "jNQXAC9IVRw.mp3"), "fake audio content");

        var result = YtDlpDownloadProvider.ParseResult(RealPrintJsonOutput, _dir);

        Assert.EndsWith("jNQXAC9IVRw.mp3", result.FilePath);
    }

    [Fact]
    public void ParseResult_ThrowsWhenNoMatchingFileExistsOnDisk()
    {
        EnsureDir();

        Assert.Throws<InvalidOperationException>(() => YtDlpDownloadProvider.ParseResult(RealPrintJsonOutput, _dir));
    }

    [Fact]
    public void ParseResult_FallsBackToUploaderWhenChannelIsAbsent()
    {
        File.WriteAllText(Path.Combine(EnsureDir(), "abc123.mp3"), "fake audio content");
        var stdout = """{"id": "abc123", "title": "Some video", "uploader": "Some Uploader"}""";

        var result = YtDlpDownloadProvider.ParseResult(stdout, _dir);

        Assert.Equal("Some Uploader", result.Channel);
    }

    private string EnsureDir()
    {
        Directory.CreateDirectory(_dir);
        return _dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
