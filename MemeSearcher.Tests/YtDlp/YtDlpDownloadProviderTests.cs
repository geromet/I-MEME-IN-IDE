using MemeSearcher.Core.Models;
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
    // audio-extraction run actually produced "jNQXAC9IVRw.mp3" on disk. DownloadAsync now also asks
    // yt-dlp for after_move:filepath so ParseResult never has to reconstruct or glob the final name.
    private const string RealPrintJsonOutput =
        """{"id": "jNQXAC9IVRw", "title": "Me at the zoo", "channel": "jawed", "uploader": "jawed", "upload_date": "20050424", "ext": "webm", "_filename": "jNQXAC9IVRw.webm"}""";

    [Fact]
    public void ParseResult_ReadsIdTitleChannelAndUploadDate()
    {
        var finalPath = CreateFile("jNQXAC9IVRw.mp3");

        var result = YtDlpDownloadProvider.ParseResult(OutputWithFinalPath(RealPrintJsonOutput, finalPath), YtDlpMediaKind.Audio);

        Assert.Equal("jNQXAC9IVRw", result.VideoId);
        Assert.Equal("Me at the zoo", result.Title);
        Assert.Equal("jawed", result.Channel);
        Assert.Equal(new DateOnly(2005, 4, 24), result.UploadDate);
    }

    [Fact]
    public void ParseResult_UsesAfterMovePathInsteadOfJsonExtension()
    {
        var finalPath = CreateFile("jNQXAC9IVRw.mp3");

        var result = YtDlpDownloadProvider.ParseResult(OutputWithFinalPath(RealPrintJsonOutput, finalPath), YtDlpMediaKind.Audio);

        Assert.Equal(finalPath, result.FilePath);
    }

    [Fact]
    public void ParseResult_IgnoresStaleSiblingFilesForSameVideoId()
    {
        CreateFile("jNQXAC9IVRw.mp3");
        var currentVideo = CreateFile("jNQXAC9IVRw.mp4");

        var result = YtDlpDownloadProvider.ParseResult(OutputWithFinalPath(RealPrintJsonOutput, currentVideo), YtDlpMediaKind.Video);

        Assert.Equal(currentVideo, result.FilePath);
        Assert.Equal(YtDlpMediaKind.Video, result.MediaKind);
    }

    [Fact]
    public void ParseResult_ThrowsWhenFinalPathIsMissingFromOutput()
    {
        Assert.Throws<InvalidOperationException>(() =>
            YtDlpDownloadProvider.ParseResult(RealPrintJsonOutput, YtDlpMediaKind.Audio));
    }

    [Fact]
    public void ParseResult_ThrowsWhenReportedFinalFileDoesNotExist()
    {
        var missing = Path.Combine(EnsureDir(), "jNQXAC9IVRw.mp3");

        Assert.Throws<InvalidOperationException>(() =>
            YtDlpDownloadProvider.ParseResult(OutputWithFinalPath(RealPrintJsonOutput, missing), YtDlpMediaKind.Audio));
    }

    [Fact]
    public void ParseResult_FallsBackToUploaderWhenChannelIsAbsent()
    {
        var finalPath = CreateFile("abc123.mp3");
        var stdout = """{"id": "abc123", "title": "Some video", "uploader": "Some Uploader"}""";

        var result = YtDlpDownloadProvider.ParseResult(OutputWithFinalPath(stdout, finalPath), YtDlpMediaKind.Audio);

        Assert.Equal("Some Uploader", result.Channel);
    }

    private static string OutputWithFinalPath(string json, string finalPath) =>
        $"{json}{Environment.NewLine}MEMESEARCHER_FINAL_PATH={finalPath}";

    private string CreateFile(string name)
    {
        var path = Path.Combine(EnsureDir(), name);
        File.WriteAllText(path, "fake media content");
        return path;
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
