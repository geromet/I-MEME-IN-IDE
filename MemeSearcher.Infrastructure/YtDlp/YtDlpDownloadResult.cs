using MemeSearcher.Core.Models;

namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>
/// What a completed yt-dlp download produced, for MediaIngestionService/orchestration to consume.
/// MediaKind reflects which form was actually requested for this download - it comes from the
/// caller, not the JSON, since yt-dlp's own output doesn't say "this was extracted as audio".
/// </summary>
public record YtDlpDownloadResult(string FilePath, string VideoId, string Title, string? Channel, DateOnly? UploadDate, YtDlpMediaKind MediaKind);
