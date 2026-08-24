using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Transcription;

/// <summary>One rendered line for the transcript viewer (#26) - a projection of Segment, not the entity itself, since the viewer never needs to write it back (read-only, per the issue's explicit scope).</summary>
public record TranscriptCue(Guid SegmentId, string Text, double? StartSeconds, double? EndSeconds);

/// <summary>
/// Reads a media's transcript for display (#26) - display-time, unlike everything else in this
/// namespace (TranscriptParserFactory etc.), which is ingestion-time. A media can in principle have
/// more than one Transcript row; this takes the most recently created one, since re-ingestion or
/// realignment operate on the existing transcript rather than creating a second one in normal use.
/// </summary>
public class TranscriptViewService(IDbContextFactory<MemeSearcherDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<TranscriptCue>?> GetCuesAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Ordered client-side: the SQLite EF Core provider can't translate ORDER BY over a
        // DateTimeOffset column into SQL (same limitation LibraryService works around).
        var transcripts = await context.Transcripts
            .AsNoTracking()
            .Where(t => t.MediaId == mediaId)
            .Include(t => t.Segments)
            .ToListAsync(cancellationToken);

        var transcript = transcripts.OrderByDescending(t => t.CreatedAt).FirstOrDefault();

        return transcript?.Segments
            .OrderBy(s => s.Sequence)
            .Select(s => new TranscriptCue(s.Id, s.Text, s.StartSeconds, s.EndSeconds))
            .ToList();
    }
}
