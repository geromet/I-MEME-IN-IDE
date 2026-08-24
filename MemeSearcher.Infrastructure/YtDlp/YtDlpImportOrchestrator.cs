using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Library;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>
/// Downloads and imports every New item of a YtDlpImportPlan, one video at a time, as the work
/// closure of a single queued Job (#14/#27). Job/IJobQueue model one atomic operation with a single
/// rolling status string, not a multi-item batch with structured per-item results - so a per-item
/// exception is caught here and turned into a persisted YtDlpImportFailure row rather than being
/// allowed to escape and fail the whole job. Progress is narrated via IProgress&lt;string&gt; the
/// same way every other queued job does.
///
/// ImportAsync accepts the download step as a delegate (defaulting to the real
/// YtDlpDownloadProvider) rather than depending on it directly - the same
/// network-touching-entry-point-vs-testable-core split as YtDlpImportPlanner's
/// PlanAsync/ClassifyAsync, so the per-item success/failure/patch logic is testable with a canned
/// download function and no real yt-dlp invocation.
/// </summary>
public class YtDlpImportOrchestrator(
    YtDlpDownloadProvider downloadProvider,
    MediaIngestionService ingestionService,
    MemeSearcherDbContext dbContext)
{
    public Task<YtDlpImportSummary> ImportAsync(
        YtDlpImportPlan plan,
        string language,
        IProgress<string> progress,
        CancellationToken cancellationToken = default) =>
        ImportAsync(plan, language, downloadProvider.DownloadAsync, progress, cancellationToken);

    public async Task<YtDlpImportSummary> ImportAsync(
        YtDlpImportPlan plan,
        string language,
        Func<string, CancellationToken, Task<YtDlpDownloadResult>> download,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        var newItems = plan.Items.Where(i => i.Status == YtDlpImportPlanStatus.New).ToList();
        var imported = 0;
        var failed = 0;

        for (var index = 0; index < newItems.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = newItems[index].Entry;

            progress.Report(
                $"[{index + 1}/{newItems.Count}] Downloading \"{entry.Title}\"... "
                + $"({imported} imported, {failed} failed so far)");

            try
            {
                var downloadResult = await download(entry.Url, cancellationToken);
                await ImportOneAsync(entry, downloadResult, language, cancellationToken);
                imported++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                await RecordFailureAsync(entry, ex.Message, cancellationToken);
            }
        }

        progress.Report($"Done: {imported} imported, {failed} failed.");
        return new YtDlpImportSummary(imported, failed);
    }

    private async Task ImportOneAsync(
        YtDlpVideoEntry entry, YtDlpDownloadResult download, string language, CancellationToken cancellationToken)
    {
        var request = new MediaIngestionRequest(download.FilePath, null, language, download.Title);
        var result = await ingestionService.ImportAsync(request, cancellationToken);

        // A dedup hit (by content hash, inside ImportAsync) against a row that never had a VideoId
        // is still worth stamping - it's the same content, now also known to be this video. A hit
        // against a row that already carries a *different* VideoId is a genuine anomaly (identical
        // content hash, two distinct video ids) - left alone rather than risking the unique index
        // or silently overwriting a fact that was already recorded.
        if (result.Media.VideoId is not null && result.Media.VideoId != download.VideoId)
        {
            throw new InvalidOperationException(
                $"Downloaded content already belongs to video '{result.Media.VideoId}' in the library.");
        }

        result.Media.VideoId = download.VideoId;
        result.Media.Channel = download.Channel;
        result.Media.UploadDate = download.UploadDate;
        result.Media.YtDlpMediaKind = download.MediaKind;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Upserts by VideoId (unique-indexed) - a video that fails on every re-run accumulates
    /// AttemptCount rather than being recorded as a fresh failure each time, per
    /// YtDlpImportFailure's own doc comment.
    /// </summary>
    private async Task RecordFailureAsync(YtDlpVideoEntry entry, string reason, CancellationToken cancellationToken)
    {
        var existing = await dbContext.YtDlpImportFailures
            .FirstOrDefaultAsync(f => f.VideoId == entry.VideoId, cancellationToken);

        if (existing is not null)
        {
            existing.Reason = reason;
            existing.FailedAt = DateTimeOffset.UtcNow;
            existing.AttemptCount++;
        }
        else
        {
            dbContext.YtDlpImportFailures.Add(new YtDlpImportFailure
            {
                Id = Guid.NewGuid(),
                VideoId = entry.VideoId,
                Title = entry.Title,
                SourceUrl = entry.Url,
                Reason = reason,
                FailedAt = DateTimeOffset.UtcNow,
                AttemptCount = 1,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
