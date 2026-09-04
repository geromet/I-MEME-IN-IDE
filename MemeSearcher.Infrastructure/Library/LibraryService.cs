using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Library;

/// <summary>
/// Read/manage operations over the persistent media corpus (addendum §1: the database is the
/// persistent index, not disposable processing state) - separate from MediaIngestionService,
/// which only ever adds to it.
/// </summary>
public class LibraryService(IDbContextFactory<MemeSearcherDbContext> dbContextFactory)
{
    public async Task<List<MediaSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Ordered client-side: the SQLite EF Core provider can't translate ORDER BY over a
        // DateTimeOffset column into SQL.
        var media = (await context.Media.ToListAsync(cancellationToken))
            .OrderByDescending(m => m.CreatedAt)
            .ToList();

        var segmentCounts = await (
            from t in context.Transcripts
            join s in context.Segments on t.Id equals s.TranscriptId
            group s by t.MediaId into g
            select new { MediaId = g.Key, Count = g.Count() }
        ).ToDictionaryAsync(x => x.MediaId, x => x.Count, cancellationToken);

        var wordStats = await (
            from t in context.Transcripts
            join s in context.Segments on t.Id equals s.TranscriptId
            join w in context.Words on s.Id equals w.SegmentId
            group w by t.MediaId into g
            select new
            {
                MediaId = g.Key,
                WordCount = g.Count(),
                PhonemizedCount = g.Count(w => w.PhonemeSequence != null),
            }
        ).ToDictionaryAsync(x => x.MediaId, x => x, cancellationToken);

        return media.Select(m =>
        {
            var words = wordStats.GetValueOrDefault(m.Id);
            return new MediaSummary(
                m.Id,
                m.Title ?? Path.GetFileName(m.Path),
                m.Path,
                m.MediaFilePath is not null,
                m.Duration,
                m.Language,
                m.CreatedAt,
                segmentCounts.GetValueOrDefault(m.Id),
                words?.WordCount ?? 0,
                words?.PhonemizedCount ?? 0,
                m.IsSelectedForSearch);
        }).ToList();
    }

    /// <summary>
    /// Milestone 13: what a search actually runs against right now, and the total to compare it
    /// against for the scope indicator ("3 of 47 sources") - one round trip serves both, since a
    /// live search and a live indicator need the same answer at the same moment.
    /// </summary>
    public async Task<(IReadOnlyList<Guid> SelectedIds, int Total)> GetSelectionSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ids = await context.Media.Select(m => new { m.Id, m.IsSelectedForSearch }).ToListAsync(cancellationToken);
        return (ids.Where(m => m.IsSelectedForSearch).Select(m => m.Id).ToList(), ids.Count);
    }

    /// <summary>
    /// #43: resolves temporary metadata facets as an intersection with the persistent corpus
    /// selection. This deliberately does not update <c>IsSelectedForSearch</c>; clearing facets
    /// therefore restores the user's saved source selection immediately.
    /// </summary>
    public async Task<(IReadOnlyList<Guid> SelectedIds, int Total)> GetSelectionSummaryAsync(
        MediaSearchFacets facets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facets);

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var media = await context.Media.ToListAsync(cancellationToken);

        return (
            media.Where(m => m.IsSelectedForSearch && facets.Matches(m)).Select(m => m.Id).ToList(),
            media.Count);
    }

    /// <summary>
    /// Milestone 17 (#20): display label for the most recent catalog applied via
    /// <see cref="ApplyCatalogScopeAsync"/>, so SearchViewModel's scope description can say
    /// "Catalog: Vine compilations (12 sources)" instead of the generic "12 of 47 source(s)" - the
    /// discriminator #20's exit criteria need ("records the catalog"). Purely a display hint, not
    /// persisted: it's cleared by any subsequent manual selection edit below, since at that point
    /// the checkbox state can no longer be said to be "exactly this catalog".
    /// </summary>
    public string? ActiveCatalogLabel { get; private set; }

    public async Task SetSelectedAsync(Guid mediaId, bool isSelected, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var media = await context.Media.FindAsync([mediaId], cancellationToken);
        if (media is null)
        {
            return;
        }

        media.IsSelectedForSearch = isSelected;
        await context.SaveChangesAsync(cancellationToken);
        ActiveCatalogLabel = null;
    }

    /// <summary>Select-all / select-none, and invert - addendum §13's affordances alongside per-row checkboxes.</summary>
    public async Task SetAllSelectedAsync(bool isSelected, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        await foreach (var media in context.Media.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            media.IsSelectedForSearch = isSelected;
        }

        await context.SaveChangesAsync(cancellationToken);
        ActiveCatalogLabel = null;
    }

    public async Task InvertSelectionAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        await foreach (var media in context.Media.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            media.IsSelectedForSearch = !media.IsSelectedForSearch;
        }

        await context.SaveChangesAsync(cancellationToken);
        ActiveCatalogLabel = null;
    }

    /// <summary>
    /// Milestone 17 (#20): "select a catalog as the active search scope" is implemented as exactly
    /// this - bulk-setting IsSelectedForSearch to match catalog membership, reusing #13's scope
    /// machinery unchanged rather than adding a new scope kind to Core (#20's explicit instruction).
    /// </summary>
    public async Task ApplyCatalogScopeAsync(IReadOnlyCollection<Guid> mediaIds, string catalogLabel, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var memberIds = mediaIds.ToHashSet();
        await foreach (var media in context.Media.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            media.IsSelectedForSearch = memberIds.Contains(media.Id);
        }

        await context.SaveChangesAsync(cancellationToken);
        ActiveCatalogLabel = catalogLabel;
    }

    /// <summary>
    /// Resolves MediaId -> playable media file path for a batch of results in one query
    /// (addendum §32: "never store only text/timestamp - store a MediaId... the application can
    /// then resolve MediaId -> media file -> timestamp"), rather than one lookup per search
    /// result. Resolves through Media.MediaFilePath, not Media.Path - a transcript-only import
    /// has no playable file, and its MediaId is simply absent from the returned dictionary rather
    /// than incorrectly mapping to the transcript file.
    /// </summary>
    public async Task<Dictionary<Guid, string>> GetPathsAsync(IEnumerable<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ids = mediaIds.Distinct().ToList();
        return await context.Media
            .Where(m => ids.Contains(m.Id) && m.MediaFilePath != null)
            .ToDictionaryAsync(m => m.Id, m => m.MediaFilePath!, cancellationToken);
    }

    /// <summary>
    /// Resolves MediaId -> display title for a batch of results in one query - used to label
    /// which source file each composite-result component came from (addendum §16/§22) with
    /// something more legible than a raw GUID.
    /// </summary>
    public async Task<Dictionary<Guid, string>> GetTitlesAsync(IEnumerable<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ids = mediaIds.Distinct().ToList();
        var media = await context.Media
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Title, m.Path })
            .ToListAsync(cancellationToken);

        return media.ToDictionary(m => m.Id, m => m.Title ?? Path.GetFileName(m.Path));
    }

    /// <summary>
    /// Removing from the library vs. deleting the source file are explicitly distinct
    /// (addendum §29) - the caller must opt into deleting the actual file on disk.
    /// </summary>
    public async Task RemoveAsync(Guid mediaId, bool deleteSourceFile, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var media = await context.Media.FindAsync([mediaId], cancellationToken);
        if (media is null)
        {
            return;
        }

        if (deleteSourceFile && File.Exists(media.Path))
        {
            File.Delete(media.Path);
        }

        // Transcript/Segment/Word/Phone cascade-delete via the FK relationships configured in
        // MemeSearcherDbContext - no need to load or remove them explicitly.
        context.Media.Remove(media);
        await context.SaveChangesAsync(cancellationToken);
    }
}