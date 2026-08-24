namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>What a completed yt-dlp download produced, for MediaIngestionService/orchestration to consume.</summary>
public record YtDlpDownloadResult(string FilePath, string VideoId, string Title, string? Channel, DateOnly? UploadDate);
