using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Search;

/// <summary>
/// Addendum §35: persists recent searches for convenience only - never a source of truth for
/// search results themselves (re-running a history entry re-runs the real search).
/// </summary>
public class SearchHistoryService(IDbContextFactory<MemeSearcherDbContext> dbContextFactory)
{
    private const int MaxRetained = 50;

    public async Task RecordAsync(
        string queryText,
        string language,
        bool isComposite,
        string scopeDescription,
        int resultCount,
        IReadOnlyCollection<Guid>? selectedMediaIds = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        context.SearchHistory.Add(new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            QueryText = queryText,
            Language = language,
            IsComposite = isComposite,
            ScopeDescription = scopeDescription,
            SelectedMediaIdsCsv = selectedMediaIds is { Count: > 0 } ids ? string.Join(',', ids) : null,
            ResultCount = resultCount,
            SearchedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        await PruneAsync(context, cancellationToken);
    }

    /// <summary>
    /// Milestone 18 (#21): records which template was run, not a reconstructed query string - a
    /// template search bypasses the phonemizer entirely and has no text/language to show.
    /// <paramref name="templateName"/> is denormalized at record time so a later rename or
    /// deletion of the template doesn't rewrite what this entry says it ran.
    /// </summary>
    public async Task RecordTemplateRunAsync(
        Guid templateId,
        string templateName,
        string scopeDescription,
        int resultCount,
        IReadOnlyCollection<Guid>? selectedMediaIds = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        context.SearchHistory.Add(new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            TemplateName = templateName,
            IsComposite = false,
            ScopeDescription = scopeDescription,
            SelectedMediaIdsCsv = selectedMediaIds is { Count: > 0 } ids ? string.Join(',', ids) : null,
            ResultCount = resultCount,
            SearchedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        await PruneAsync(context, cancellationToken);
    }

    /// <summary>
    /// Text-search history only (TemplateId null) - SearchViewModel's rerun path assumes a
    /// QueryText/Language to restore (#21: a template-driven entry has neither). Template runs
    /// have their own list; see GetRecentTemplateRunsAsync.
    /// </summary>
    public async Task<List<SearchHistoryEntry>> GetRecentAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Ordered client-side: the SQLite EF Core provider can't translate ORDER BY over a
        // DateTimeOffset column into SQL (same issue as LibraryService/CompositeSearchService).
        var entries = await context.SearchHistory.Where(h => h.TemplateId == null).ToListAsync(cancellationToken);
        return entries.OrderByDescending(h => h.SearchedAt).Take(count).ToList();
    }

    /// <summary>Milestone 18 (#21) counterpart to GetRecentAsync, for the Templates panel's own recent-runs list.</summary>
    public async Task<List<SearchHistoryEntry>> GetRecentTemplateRunsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entries = await context.SearchHistory.Where(h => h.TemplateId != null).ToListAsync(cancellationToken);
        return entries.OrderByDescending(h => h.SearchedAt).Take(count).ToList();
    }

    /// <summary>
    /// Keeps the table itself from growing without bound over a long-running session. The 50-row
    /// budget is shared across text-search and template-run entries (#21) - a session heavy on one
    /// kind can evict all history of the other kind, since GetRecentAsync/GetRecentTemplateRunsAsync
    /// each read only their own subset of whatever survives here. Deliberate, not yet split
    /// per-kind: revisit if that turns out to surprise anyone in practice.
    /// </summary>
    private static async Task PruneAsync(MemeSearcherDbContext context, CancellationToken cancellationToken)
    {
        var all = await context.SearchHistory.ToListAsync(cancellationToken);
        var stale = all.OrderByDescending(h => h.SearchedAt).Skip(MaxRetained).ToList();

        if (stale.Count == 0)
        {
            return;
        }

        context.SearchHistory.RemoveRange(stale);
        await context.SaveChangesAsync(cancellationToken);
    }
}
