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
    /// <summary>
    /// What to do for one media item, decided by <see cref="PlanMediaAsync"/> before any transcript
    /// is loaded. <c>PostingsByNGram is null</c> means "load and scan the whole stream" - either
    /// filtering isn't possible for this query/options at all, or this media has never been
    /// indexed (#9's degrade-to-full-scan guarantee: unindexed must not mean unsearched). A
    /// non-null dictionary means "load, but only run the matcher over windows seeded by these
    /// postings". Media with postings but zero candidates for this query don't get a plan entry at
    /// all - see PlanMediaAsync.
    /// </summary>
    private readonly record struct MediaSearchPlan(Guid MediaId, IReadOnlyDictionary<string, IReadOnlyList<int>>? PostingsByNGram);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string queryText,
        string language,
        SearchScope scope,
        SearchMode mode = SearchMode.SimilarPhonetic,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var phonemizedQuery = await queryCache.GetOrAddAsync(
            queryText, language, ct => phonemizer.PhonemizeAsync(queryText, language, ct), cancellationToken);

        return await SearchAsync(PhoneStreamBuilder.BuildQueryTokens(phonemizedQuery), scope, mode, options, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        IReadOnlyList<PhoneToken> queryTokens,
        SearchScope scope,
        SearchMode mode = SearchMode.SimilarPhonetic,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= PhoneticSearchOptions.ForMode(mode);

        var queryPhonemeCount = queryTokens.Count(t => !t.IsBoundary);

        if (queryTokens.Count == 0 || queryPhonemeCount == 0)
        {
            return [];
        }

        var mediaIds = await ResolveMediaIdsAsync(scope, cancellationToken);
        if (mediaIds.Count == 0)
        {
            return [];
        }

        // Expansion and padding are properties of the query and options alone, not of any one
        // media - computed once here and reused for every media below, rather than recomputed (or
        // re-queried) per media the way the first version of this did.
        var queryNGrams = PhoneNGramIndexer.Extract(queryTokens).Select(o => o.NGram).ToHashSet();
        var padding = queryNGrams.Count == 0 ? null : PhoneNGramCandidateGenerator.SafePadding(queryTokens.Count, options);
        var expandedNGrams = padding is null ? null : PhoneNGramCandidateGenerator.ExpandFuzzy(queryNGrams, options, queryTokens.Count);

        var plans = await PlanMediaAsync(mediaIds, expandedNGrams, cancellationToken);

        var perMediaResults = await Task.WhenAll(
            plans.Select(plan => SearchMediaAsync(
                plan, queryTokens, queryPhonemeCount, padding, expandedNGrams, options, cancellationToken)));

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

    /// <summary>
    /// Decides, for every scoped media, whether it needs to be loaded at all - *before* any
    /// transcript is loaded (#9, corrected: the original version still loaded and rebuilt every
    /// scoped media's full stream up front and only narrowed the DP step inside it, which measured
    /// as no speedup at all - see issue #9's benchmark comment. The actual win is not touching a
    /// media's transcript in the first place when it has no candidate for this query).
    ///
    /// One batched query across every scoped media, not one per media: the original per-media
    /// postings round trip was itself part of the overhead this was supposed to remove.
    /// </summary>
    private async Task<List<MediaSearchPlan>> PlanMediaAsync(
        List<Guid> mediaIds, HashSet<string>? expandedNGrams, CancellationToken cancellationToken)
    {
        if (expandedNGrams is null)
        {
            // Filtering isn't possible for this query/options at all (too short for a trigram, or
            // options SafePadding can't bound) - every scoped media must be loaded and scanned in
            // full, exactly like before #9. No postings query needed since nothing would use it.
            return mediaIds.Select(id => new MediaSearchPlan(id, null)).ToList();
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Two queries, not one - filtering by NGram here is what makes this a narrow lookup
        // instead of pulling every posting for every scoped media into memory (89k rows at 100
        // media in testing, most of them thrown away unread). But that filter alone can't
        // distinguish "never indexed" from "indexed, zero candidates for this query" - both would
        // simply be absent from the result - so which media are indexed at all is asked for
        // separately, to keep the degrade-to-full-scan guarantee correct.
        var indexedMediaIds = await context.PhoneNGramPostings
            .Where(p => mediaIds.Contains(p.MediaId))
            .Select(p => p.MediaId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var postings = await context.PhoneNGramPostings
            .Where(p => mediaIds.Contains(p.MediaId) && expandedNGrams.Contains(p.NGram))
            .Select(p => new { p.MediaId, p.NGram, p.StreamPosition })
            .ToListAsync(cancellationToken);

        var indexedMediaIdSet = indexedMediaIds.ToHashSet();
        var postingsByMedia = postings
            .GroupBy(p => p.MediaId)
            .ToDictionary(
                mediaGroup => mediaGroup.Key,
                mediaGroup => mediaGroup
                    .GroupBy(p => p.NGram)
                    .ToDictionary(ngramGroup => ngramGroup.Key, IReadOnlyList<int> (ngramGroup) => ngramGroup.Select(p => p.StreamPosition).ToList()));

        var plans = new List<MediaSearchPlan>();
        foreach (var mediaId in mediaIds)
        {
            if (!indexedMediaIdSet.Contains(mediaId))
            {
                // Never indexed (or indexed before this media had a transcript) - degrade to a
                // full scan rather than silently reporting zero results for it.
                plans.Add(new MediaSearchPlan(mediaId, null));
                continue;
            }

            if (!postingsByMedia.TryGetValue(mediaId, out var postingsByNGram))
            {
                // Indexed, but genuinely has no candidate for this query even after fuzzy
                // expansion (the second query above filtered exactly this away) - the filter
                // doing its job. No plan entry at all: this media's transcript is never loaded,
                // never rebuilt into a stream, never scanned.
                continue;
            }

            plans.Add(new MediaSearchPlan(mediaId, postingsByNGram));
        }

        return plans;
    }

    private async Task<List<SearchResult>> SearchMediaAsync(
        MediaSearchPlan plan,
        IReadOnlyList<PhoneToken> queryTokens,
        int queryPhonemeCount,
        int? padding,
        HashSet<string>? expandedNGrams,
        PhoneticSearchOptions options,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var transcripts = await context.Transcripts
            .AsNoTracking()
            .Where(t => t.MediaId == plan.MediaId)
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

        var matches = plan.PostingsByNGram is null
            ? PhoneticSequenceMatcher.FindMatches(queryTokens, candidateTokens, options)
            : WindowedFindMatches(queryTokens, candidateTokens, plan.PostingsByNGram, expandedNGrams!, padding!.Value, options);

        return matches
            .Select(match => ToSearchResult(plan.MediaId, match, candidateStream, queryTokens, queryPhonemeCount, options))
            .Where(r => r.Score >= options.MinimumScore)
            .ToList();
    }

    /// <summary>
    /// Candidate generation (#9): narrows the O(query * candidate) DP to windows around this
    /// media's own n-gram hits, instead of running it over the whole stream.
    /// </summary>
    private static IReadOnlyList<PhoneticMatchSpan> WindowedFindMatches(
        IReadOnlyList<PhoneToken> queryTokens,
        List<PhoneToken> candidateTokens,
        IReadOnlyDictionary<string, IReadOnlyList<int>> postingsByNGram,
        HashSet<string> expandedNGrams,
        int padding,
        PhoneticSearchOptions options)
    {
        // PlanMediaAsync only produces this plan when at least one expanded n-gram is present in
        // postingsByNGram, so GenerateWindows cannot return null (that only happens for an empty
        // n-gram set) - it can still return an empty list, which would mean the same "no candidate
        // anywhere" outcome PlanMediaAsync already filters out before this is ever called.
        var windows = PhoneNGramCandidateGenerator.GenerateWindows(
            expandedNGrams,
            ngram => postingsByNGram.GetValueOrDefault(ngram, []),
            candidateTokens.Count,
            padding)!;

        // Each window is its own FindMatches call with its own local-minima suppression, so a
        // below-threshold run straddling two windows would otherwise surface once per window
        // instead of the single match a full-stream call would report for it (#9).
        var rawMatches = windows.SelectMany(window => RunWindow(queryTokens, candidateTokens, window, options));
        return PhoneticSequenceMatcher.MergeAdjacentMatches(rawMatches);
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
                // #15: AlignmentSteps carries its own candidate indices that need the same window
                // offset as Correspondences above - a step's CandidateIndex is null for a
                // QueryExtra step, so only offset when it's actually present.
                AlignmentSteps = match.AlignmentSteps
                    .Select(s => s with { CandidateIndex = s.CandidateIndex + window.Start })
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

        var sourceText = PhoneStreamTextBuilder.BuildSourceText(phonemeEntries);
        var ipa = PhoneStreamTextBuilder.BuildIpa(phonemeEntries);
        var matchPhonemes = phonemeEntries.Select(e => e.Token.Symbol).ToList();
        var queryPhonemes = queryTokens.Where(t => !t.IsBoundary).Select(t => t.Symbol).ToList();

        var matchedPhoneDetails = phonemeEntries
            .Select(e => new MatchedPhone(e.Token.Symbol, e.StartSeconds, e.EndSeconds, e.IsPhoneLevelAligned))
            .ToList();

        // Word-boundary tokens align cheaply against each other (SubstitutionCost gives them a
        // free pass) and show up as spurious Match steps here - filtered out, since they're a
        // structural device the matcher uses internally, not a phoneme correspondence a user
        // should see (#15).
        var queryIndexMap = QueryCoverage.BuildIndexMap(queryTokens);
        var alignmentSteps = match.AlignmentSteps
            .Where(s =>
                (s.QueryIndex is not int qi || !queryTokens[qi].IsBoundary) &&
                (s.CandidateIndex is not int ci || !candidateStream[ci].Token.IsBoundary))
            .Select(s => new QueryAlignmentStep(
                s.Op,
                s.QueryIndex is int qi2 ? queryTokens[qi2].Symbol : null,
                s.CandidateIndex is int ci2 ? candidateStream[ci2].Token.Symbol : null,
                s.QueryIndex is int qi3 ? queryIndexMap[qi3] : null))
            .ToList();

        // #25: the envelope of positions this match actually corresponds to (Match/Substitute),
        // as opposed to ones the query asked for that this candidate lacks entirely (QueryExtra) -
        // the coverage a result-row strip needs to show, since the matcher aligns the whole query
        // against every candidate rather than only the part that's actually present.
        var coveredIndices = alignmentSteps
            .Where(s => s.Op is AlignmentOp.Match or AlignmentOp.Substitute)
            .Select(s => s.QueryIndex!.Value);
        var (queryStart, queryEnd) = QueryCoverage.ComputeSpan(coveredIndices);

        var score = queryPhonemeCount > 0 && !double.IsPositiveInfinity(options.SubstitutionMaxCost)
            ? Math.Clamp(1 - match.Cost / (queryPhonemeCount * options.SubstitutionMaxCost), 0, 1)
            : match.Cost == 0 ? 1.0 : 0.0;

        return new SearchResult(
            mediaId, startSeconds, endSeconds, sourceText, ipa, matchPhonemes, score, queryPhonemes,
            matchedPhoneDetails, alignmentSteps, queryStart, queryEnd);
    }

}
