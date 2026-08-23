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
                words?.PhonemizedCount ?? 0);
        }).ToList();
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
