using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.YtDlp;

/// <summary>
/// Turns an enumerated channel/playlist into a reviewable plan (#27), by classifying every entry
/// against what the corpus already knows: a video already imported (Media.VideoId), one that
/// permanently failed on a previous run (YtDlpImportFailures), or one that's genuinely new. This is
/// the "enumerate, diff against stored ids, download only what's new" incremental-re-run
/// requirement, and it runs entirely before any download starts.
/// </summary>
public class YtDlpImportPlanner(YtDlpPlaylistEnumerationService enumerationService, IDbContextFactory<MemeSearcherDbContext> dbContextFactory)
{
    public async Task<YtDlpImportPlan> PlanAsync(string url, CancellationToken cancellationToken = default)
    {
        var entries = await enumerationService.EnumerateAsync(url, cancellationToken);
        return await ClassifyAsync(entries, cancellationToken);
    }

    /// <summary>The classification half of PlanAsync, exposed separately so it's testable against a real database without needing live network access to enumerate anything first.</summary>
    public async Task<YtDlpImportPlan> ClassifyAsync(IReadOnlyList<YtDlpVideoEntry> entries, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var videoIds = entries.Select(e => e.VideoId).ToList();

        var importedIds = await context.Media
            .Where(m => m.VideoId != null && videoIds.Contains(m.VideoId))
            .Select(m => m.VideoId!)
            .ToListAsync(cancellationToken);
        var importedSet = importedIds.ToHashSet();

        var failedIds = await context.YtDlpImportFailures
            .Where(f => videoIds.Contains(f.VideoId))
            .Select(f => f.VideoId)
            .ToListAsync(cancellationToken);
        var failedSet = failedIds.ToHashSet();

        var items = entries
            .Select(entry => new YtDlpImportPlanItem(
                entry,
                importedSet.Contains(entry.VideoId) ? YtDlpImportPlanStatus.AlreadyImported
                    : failedSet.Contains(entry.VideoId) ? YtDlpImportPlanStatus.PreviouslyFailed
                    : YtDlpImportPlanStatus.New))
            .ToList();

        return new YtDlpImportPlan(items);
    }
}
