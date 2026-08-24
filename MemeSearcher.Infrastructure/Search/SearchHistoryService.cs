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

    public async Task<List<SearchHistoryEntry>> GetRecentAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Ordered client-side: the SQLite EF Core provider can't translate ORDER BY over a
        // DateTimeOffset column into SQL (same issue as LibraryService/CompositeSearchService).
        var entries = await context.SearchHistory.ToListAsync(cancellationToken);
        return entries.OrderByDescending(h => h.SearchedAt).Take(count).ToList();
    }

    /// <summary>Keeps the table itself from growing without bound over a long-running session.</summary>
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
