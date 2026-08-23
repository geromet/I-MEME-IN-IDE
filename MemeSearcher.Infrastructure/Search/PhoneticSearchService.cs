using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Search;

/// <summary>
/// Ties the pure Core algorithm (PhoneStreamBuilder, PhoneticSequenceMatcher) to the database:
/// resolves a SearchScope to media IDs, loads each one's transcript, and searches selected media
/// in parallel (addendum §23) rather than one at a time. Takes a context factory rather than a
/// single DbContext because EF Core's DbContext isn't safe to use concurrently across the
/// parallel per-media tasks below.
/// </summary>
public class PhoneticSearchService(
    IDbContextFactory<MemeSearcherDbContext> dbContextFactory,
    IPhonemizer phonemizer,
    IQueryPhonemizationCache queryCache) : IPhoneticSearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string queryText,
        string language,
        SearchScope scope,
        SearchMode mode = SearchMode.SimilarPhonetic,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= PhoneticSearchOptions.ForMode(mode);

        var phonemizedQuery = await queryCache.GetOrAddAsync(
            queryText, language, ct => phonemizer.PhonemizeAsync(queryText, language, ct), cancellationToken);
        var queryTokens = PhoneStreamBuilder.BuildQueryTokens(phonemizedQuery);
        var queryPhonemeCount = queryTokens.Count(t => !t.IsBoundary);

        if (queryTokens.Count == 0 || queryPhonemeCount == 0)
        {
            return [];
        }

        var mediaIds = await ResolveMediaIdsAsync(scope, cancellationToken);

        var perMediaResults = await Task.WhenAll(
            mediaIds.Select(mediaId => SearchMediaAsync(mediaId, queryTokens, queryPhonemeCount, options, cancellationToken)));

        return perMediaResults
            .SelectMany(results => results)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => (r.EndSeconds - r.StartSeconds))
            .ThenBy(r => r.MediaId)
            .ThenBy(r => r.StartSeconds)
            .Take(options.MaxResults)
            .ToList();
    }

    private async Task<List<Guid>> ResolveMediaIdsAsync(SearchScope scope, CancellationToken cancellationToken)
    {
        if (scope is SearchScope.SelectedMedia selected)
        {
            return selected.MediaIds.ToList();
        }

        if (scope is SearchScope.SingleMedia single)
        {
            return [single.MediaId];
        }

        if (scope is SearchScope.AllIndexedMedia)
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Media.Select(m => m.Id).ToListAsync(cancellationToken);
        }

        throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
    }

    private async Task<List<SearchResult>> SearchMediaAsync(
        Guid mediaId,
        IReadOnlyList<PhoneToken> queryTokens,
        int queryPhonemeCount,
        PhoneticSearchOptions options,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var transcripts = await context.Transcripts
            .Where(t => t.MediaId == mediaId)
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .ToListAsync(cancellationToken);

        if (transcripts.Count == 0)
        {
            return [];
        }

        var candidateStream = PhoneStreamBuilder.Build(transcripts);
        var candidateTokens = candidateStream.Select(e => e.Token).ToList();

        var matches = PhoneticSequenceMatcher.FindMatches(queryTokens, candidateTokens, options);

        return matches
            .Select(match => ToSearchResult(mediaId, match, candidateStream, queryTokens, queryPhonemeCount, options))
            .Where(r => r.Score >= options.MinimumScore)
            .ToList();
    }

    private static SearchResult ToSearchResult(
        Guid mediaId,
        PhoneticMatchSpan match,
        List<PhoneStreamEntry> candidateStream,
        IReadOnlyList<PhoneToken> queryTokens,
        int queryPhonemeCount,
        PhoneticSearchOptions options)
    {
        var matchedEntries = candidateStream.Skip(match.Start).Take(match.End - match.Start).ToList();
        var phonemeEntries = matchedEntries.Where(e => !e.Token.IsBoundary).ToList();

        var startSeconds = phonemeEntries.Count > 0 ? phonemeEntries[0].StartSeconds!.Value : 0;
        var endSeconds = phonemeEntries.Count > 0 ? phonemeEntries[^1].EndSeconds!.Value : 0;

        var sourceText = string.Join(' ', DistinctConsecutiveWords(phonemeEntries));
        var ipa = string.Join(' ', GroupByWord(phonemeEntries).Select(g => string.Concat(g)));
        var matchPhonemes = phonemeEntries.Select(e => e.Token.Symbol).ToList();
        var queryPhonemes = queryTokens.Where(t => !t.IsBoundary).Select(t => t.Symbol).ToList();

        var score = queryPhonemeCount > 0 && !double.IsPositiveInfinity(options.SubstitutionMaxCost)
            ? Math.Clamp(1 - match.Cost / (queryPhonemeCount * options.SubstitutionMaxCost), 0, 1)
            : match.Cost == 0 ? 1.0 : 0.0;

        return new SearchResult(mediaId, startSeconds, endSeconds, sourceText, ipa, matchPhonemes, queryPhonemes, score);
    }

    private static IEnumerable<string> DistinctConsecutiveWords(IReadOnlyList<PhoneStreamEntry> phonemeEntries)
    {
        Guid? lastWordId = null;
        foreach (var entry in phonemeEntries)
        {
            if (entry.WordId != lastWordId)
            {
                yield return entry.WordText!;
                lastWordId = entry.WordId;
            }
        }
    }

    private static IEnumerable<IEnumerable<string>> GroupByWord(IReadOnlyList<PhoneStreamEntry> phonemeEntries)
    {
        var currentWordId = (Guid?)null;
        var currentGroup = new List<string>();

        foreach (var entry in phonemeEntries)
        {
            if (entry.WordId != currentWordId && currentGroup.Count > 0)
            {
                yield return currentGroup;
                currentGroup = [];
            }

            currentGroup.Add(entry.Token.Symbol);
            currentWordId = entry.WordId;
        }

        if (currentGroup.Count > 0)
        {
            yield return currentGroup;
        }
    }
}
