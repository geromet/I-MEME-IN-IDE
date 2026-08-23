using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Search;

/// <summary>
/// EF Core-backed implementation of <see cref="IPhoneNGramIndexService"/> (#9). Builds postings by
/// running the same <c>PhoneStreamBuilder</c>/<c>PhoneNGramIndexer</c> pipeline the search path
/// uses to build a candidate stream, so the index can never disagree with what search itself would
/// compute from the same rows.
/// </summary>
public class PhoneNGramIndexService(IDbContextFactory<MemeSearcherDbContext> dbContextFactory) : IPhoneNGramIndexService
{
    public async Task IndexMediaAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var transcripts = await context.Transcripts
            .AsNoTracking()
            .Where(t => t.MediaId == mediaId)
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .ThenInclude(w => w.Phones)
            .ToListAsync(cancellationToken);

        // Deleted unconditionally, even when there is nothing to replace them with: a media item
        // whose transcript was removed, or a realignment that emptied a word's phones, must not
        // leave a previous version's postings searchable (#9's "must not go stale" requirement).
        var existing = context.PhoneNGramPostings.Where(p => p.MediaId == mediaId);
        context.PhoneNGramPostings.RemoveRange(existing);

        if (transcripts.Count > 0)
        {
            var stream = PhoneStreamBuilder.Build(transcripts);
            var tokens = stream.Select(entry => entry.Token).ToList();
            var occurrences = PhoneNGramIndexer.Extract(tokens);

            context.PhoneNGramPostings.AddRange(occurrences.Select(occurrence => new PhoneNGramPosting
            {
                Id = Guid.NewGuid(),
                MediaId = mediaId,
                NGram = occurrence.NGram,
                StreamPosition = occurrence.Position,
            }));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReindexSummary> ReindexAllAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> mediaIds;
        await using (var context = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            mediaIds = await context.Media.Select(m => m.Id).ToListAsync(cancellationToken);
        }

        foreach (var mediaId in mediaIds)
        {
            await IndexMediaAsync(mediaId, cancellationToken);
        }

        await using var countContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var postingCount = await countContext.PhoneNGramPostings.CountAsync(cancellationToken);

        return new ReindexSummary(mediaIds.Count, postingCount);
    }
}
