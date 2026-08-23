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
            // Phones are loaded because PhoneStreamBuilder now prefers them over the predicted
            // Word.PhonemeSequence where an alignment has run (#18). Without this Include they
            // come back empty and the builder silently falls back to the prediction - the exact
            // "aligned data is inert for search" bug being fixed.
            .ThenInclude(w => w.Phones)
            .ToListAsync(cancellationToken);

        if (transcripts.Count == 0)
        {
            return [];
        }

        var candidateStream = PhoneStreamBuilder.Build(transcripts);
        var candidateTokens = candidateStream.Select(e => e.Token).ToList();

        var matches = await FindMatchesAsync(context, mediaId, queryTokens, candidateTokens, options, cancellationToken);

        return matches
            .Select(match => ToSearchResult(mediaId, match, candidateStream, queryTokens, queryPhonemeCount, options))
            .Where(r => r.Score >= options.MinimumScore)
            .ToList();
    }

    /// <summary>
    /// Candidate generation (#9): narrows the O(query * candidate) DP to windows around n-gram
    /// hits, instead of running it over the whole media. Degrades to the pre-#9 full-stream scan -
    /// not to zero results - whenever it can't safely filter: a query too short to form even one
    /// trigram, or a media item with no postings at all (never indexed, or indexed before this
    /// media had a transcript). A media *with* postings that genuinely has no candidate for this
    /// query, even after fuzzy n-gram expansion, correctly returns no matches for it - that is the
    /// filter doing its job, not a missing-index case.
    /// </summary>
    private static async Task<IReadOnlyList<PhoneticMatchSpan>> FindMatchesAsync(
        MemeSearcherDbContext context,
        Guid mediaId,
        IReadOnlyList<PhoneToken> queryTokens,
        List<PhoneToken> candidateTokens,
        PhoneticSearchOptions options,
        CancellationToken cancellationToken)
    {
        var queryNGrams = PhoneNGramIndexer.Extract(queryTokens).Select(o => o.NGram).ToHashSet();
        if (queryNGrams.Count == 0)
        {
            return PhoneticSequenceMatcher.FindMatches(queryTokens, candidateTokens, options);
        }

        // Bound *before* touching the database: these options simply cannot be windowed safely
        // (e.g. a caller-supplied InsertionCost of 0), so there is nothing a postings lookup could
        // do to help - skip straight to the pre-#9 full scan.
        var padding = PhoneNGramCandidateGenerator.SafePadding(queryTokens.Count, options);
        if (padding is null)
        {
            return PhoneticSequenceMatcher.FindMatches(queryTokens, candidateTokens, options);
        }

        var postings = await context.PhoneNGramPostings
            .Where(p => p.MediaId == mediaId)
            .Select(p => new { p.NGram, p.StreamPosition })
            .ToListAsync(cancellationToken);

        if (postings.Count == 0)
        {
            return PhoneticSequenceMatcher.FindMatches(queryTokens, candidateTokens, options);
        }

        var postingsByNGram = postings
            .GroupBy(p => p.NGram)
            .ToDictionary(g => g.Key, IReadOnlyList<int> (g) => g.Select(p => p.StreamPosition).ToList());

        var expandedNGrams = PhoneNGramCandidateGenerator.ExpandFuzzy(queryNGrams, options.SubstitutionMaxCost);

        // ExpandFuzzy only ever adds to a non-empty set (queryNGrams.Count > 0 was just checked),
        // so GenerateWindows cannot return null here - it can still return an empty list, meaning
        // "looked, found no candidate anywhere", which is a real (measured, not assumed) outcome.
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            expandedNGrams,
            ngram => postingsByNGram.GetValueOrDefault(ngram, []),
            candidateTokens.Count,
            padding: padding.Value)!;

        return windows
            .SelectMany(window => RunWindow(queryTokens, candidateTokens, window, options))
            .ToList();
    }

    private static IEnumerable<PhoneticMatchSpan> RunWindow(
        IReadOnlyList<PhoneToken> queryTokens,
        List<PhoneToken> candidateTokens,
        PhoneNGramCandidateGenerator.Window window,
        PhoneticSearchOptions options)
    {
        if (window.Length == 0)
        {
            yield break;
        }

        var slice = candidateTokens.GetRange(window.Start, window.Length);

        foreach (var match in PhoneticSequenceMatcher.FindMatches(queryTokens, slice, options))
        {
            yield return match with
            {
                Start = match.Start + window.Start,
                End = match.End + window.Start,
                Correspondences = match.Correspondences
                    .Select(c => (c.QueryIndex, c.CandidateIndex + window.Start))
                    .ToList(),
            };
        }
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

        // Null propagates rather than collapsing to 0 (#32). These unwraps were safe only while a
        // stored timing could never be null; a match inside a plain-text transcript now genuinely
        // has none, and forcing it to zero is the exact fabrication that issue removed. The
        // empty-match branch is null for the same reason - "no phonemes matched" is not "matched
        // at the start of the file".
        var startSeconds = phonemeEntries.Count > 0 ? phonemeEntries[0].StartSeconds : null;
        var endSeconds = phonemeEntries.Count > 0 ? phonemeEntries[^1].EndSeconds : null;

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
