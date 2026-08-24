using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Search;

/// <summary>
/// Milestone 4: assembles a query out of clips from multiple source files. Built on the same
/// PhoneStreamBuilder/PhoneticSequenceMatcher as single-source search - the only real difference
/// is that the candidate stream here is BuildComposite's concatenation of every scoped media's
/// stream (joined by cross-file boundaries) instead of one media's stream at a time, so this
/// can't be parallelized across media the way PhoneticSearchService is: composite matches need
/// the whole combined candidate array in one DP pass to find alignments that cross files at all.
/// </summary>
public class CompositeSearchService(
    IDbContextFactory<MemeSearcherDbContext> dbContextFactory,
    IPhonemizer phonemizer,
    IQueryPhonemizationCache queryCache) : ICompositeSearchService
{
    public async Task<IReadOnlyList<CompositeSearchResult>> SearchAsync(
        string queryText,
        string language,
        SearchScope scope,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= PhoneticSearchOptions.ForMode(SearchMode.SimilarPhonetic);

        var phonemizedQuery = await queryCache.GetOrAddAsync(
            queryText, language, ct => phonemizer.PhonemizeAsync(queryText, language, ct), cancellationToken);
        var queryTokens = PhoneStreamBuilder.BuildQueryTokens(phonemizedQuery);
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

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Milestone 10 (#4/#10): fixed concatenation order can only stitch files in the order given
        // to it, so a query that needs files in a different order than they were imported/created
        // is invisible to it no matter how good a match it would be. Resolved *before* transcripts
        // are loaded, off #9's n-gram postings, so a corpus with no matching index entries for this
        // query costs one extra (usually empty) query rather than a wasted second DP pass.
        var candidateOrder = options.UseCandidateOrdering
            ? await ResolveCandidateOrderAsync(context, mediaIds, queryTokens, options, cancellationToken)
            : null;

        var transcripts = await context.Transcripts
            .Where(t => mediaIds.Contains(t.MediaId))
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            // Phones are loaded because PhoneStreamBuilder now prefers them over the predicted
            // Word.PhonemeSequence where an alignment has run (#18). Without this Include they
            // come back empty and the builder silently falls back to the prediction - the exact
            // "aligned data is inert for search" bug being fixed.
            .ThenInclude(w => w.Phones)
            .ToListAsync(cancellationToken);

        var transcriptsByMedia = transcripts.ToLookup(t => t.MediaId);

        var results = RunPass(mediaIds, transcriptsByMedia, queryTokens, queryPhonemeCount, options);

        // Only worth a second DP pass when there is a genuinely different, multi-file order to try -
        // a single candidate media, or none at all, cannot produce a composite result on its own
        // that the fixed-order pass above didn't already have the same chance to find.
        if (candidateOrder is { Count: >= 2 })
        {
            results.AddRange(RunPass(candidateOrder, transcriptsByMedia, queryTokens, queryPhonemeCount, options));
        }

        return DeduplicateAndRank(results, options);
    }

    private static List<CompositeSearchResult> RunPass(
        IReadOnlyList<Guid> mediaOrder,
        ILookup<Guid, Transcript> transcriptsByMedia,
        IReadOnlyList<PhoneToken> queryTokens,
        int queryPhonemeCount,
        PhoneticSearchOptions options)
    {
        var candidateStream = PhoneStreamBuilder.BuildComposite(mediaOrder.Select(id => transcriptsByMedia[id]));
        var candidateTokens = candidateStream.Select(e => e.Token).ToList();

        if (candidateTokens.Count == 0)
        {
            return [];
        }

        var matches = PhoneticSequenceMatcher.FindMatches(queryTokens, candidateTokens, options);

        return matches
            .Select(match => ToCompositeResult(match, candidateStream, queryTokens, queryPhonemeCount, options))
            .Where(r => r is not null && r.OverallScore >= options.MinimumScore)
            .Select(r => r!)
            .ToList();
    }

    /// <summary>
    /// One additional media order to try, on top of the fixed one <see cref="ResolveMediaIdsAsync"/>
    /// returns: sort every media that has *any* #9 candidate-n-gram evidence for this query by the
    /// earliest query position that evidence corresponds to. A media whose only evidence is near the
    /// query's start belongs early in the concatenation; one whose evidence is near the end belongs
    /// late - which is exactly the ordering the "superman" split-across-files case needs regardless
    /// of which file was imported/created first.
    ///
    /// Deliberately one derived ordering, not a search over permutations: bounding a permutation
    /// count by MaxSourceFiles does not bound the number of *candidate* media to permute (a corpus
    /// can have far more than MaxSourceFiles media with some evidence for a query), so trying every
    /// ordering of them is combinatorial. This is O(candidates log candidates) instead.
    ///
    /// Returns null when candidate generation cannot help (no query n-grams, e.g. ExactPhonetic on
    /// a 1-2 phoneme query) or fewer than two media have any evidence - in both cases there is
    /// nothing for an alternate order to improve on and the caller falls back to the fixed order
    /// alone, matching #9's own "can't filter, don't guess" rule for candidate generation.
    /// </summary>
    private static async Task<List<Guid>?> ResolveCandidateOrderAsync(
        MemeSearcherDbContext context,
        IReadOnlyList<Guid> mediaIds,
        IReadOnlyList<PhoneToken> queryTokens,
        PhoneticSearchOptions options,
        CancellationToken cancellationToken)
    {
        var occurrences = PhoneNGramIndexer.Extract(queryTokens);
        if (occurrences.Count == 0)
        {
            return null;
        }

        // Expanded per-occurrence (not once over the whole query, as PhoneticSearchService does),
        // because an ordering needs to know *which query position* each matching variant came from -
        // information a single merged expanded set would lose.
        var variantToQueryPositions = new Dictionary<string, List<int>>();
        foreach (var occurrence in occurrences)
        {
            foreach (var variant in PhoneNGramCandidateGenerator.ExpandFuzzy([occurrence.NGram], options, queryTokens.Count))
            {
                if (!variantToQueryPositions.TryGetValue(variant, out var positions))
                {
                    positions = [];
                    variantToQueryPositions[variant] = positions;
                }

                positions.Add(occurrence.Position);
            }
        }

        if (variantToQueryPositions.Count == 0)
        {
            return null;
        }

        var variants = variantToQueryPositions.Keys.ToList();
        var postings = await context.PhoneNGramPostings
            .Where(p => mediaIds.Contains(p.MediaId) && variants.Contains(p.NGram))
            .Select(p => new { p.MediaId, p.NGram })
            .ToListAsync(cancellationToken);

        var minQueryPositionByMedia = new Dictionary<Guid, int>();
        foreach (var posting in postings)
        {
            foreach (var position in variantToQueryPositions[posting.NGram])
            {
                if (!minQueryPositionByMedia.TryGetValue(posting.MediaId, out var existing) || position < existing)
                {
                    minQueryPositionByMedia[posting.MediaId] = position;
                }
            }
        }

        if (minQueryPositionByMedia.Count < 2)
        {
            return null;
        }

        return minQueryPositionByMedia
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>
    /// Trying more than one media order (see <see cref="ResolveCandidateOrderAsync"/>) can find the
    /// same real match twice - identified by its exact source spans, not object identity, since each
    /// pass builds its own <see cref="CompositeSearchResult"/>. Keeping the first-seen copy is
    /// arbitrary but harmless: both copies score identically because they describe the same
    /// candidate-stream slice.
    /// </summary>
    private static List<CompositeSearchResult> DeduplicateAndRank(List<CompositeSearchResult> results, PhoneticSearchOptions options)
    {
        var seen = new HashSet<string>();
        var deduped = new List<CompositeSearchResult>();

        foreach (var result in results)
        {
            var signature = string.Join('|', result.Components.Select(c => $"{c.MediaId}:{c.StartSeconds}:{c.EndSeconds}"));
            if (seen.Add(signature))
            {
                deduped.Add(result);
            }
        }

        return deduped
            .OrderByDescending(r => r.OverallScore)
            .ThenBy(r => r.Components.Count)
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

            // Deterministic order matters here in a way it doesn't for single-source search:
            // composite matching can only stitch files together in the order the candidate
            // stream concatenates them (see PhoneStreamBuilder.BuildComposite), so an
            // unspecified database order can silently prevent an otherwise-good match from
            // being found. Ordered client-side: the SQLite EF Core provider can't translate
            // ORDER BY over a DateTimeOffset column into SQL (same issue as LibraryService).
            var media = await context.Media.Select(m => new { m.Id, m.CreatedAt }).ToListAsync(cancellationToken);
            return media.OrderBy(m => m.CreatedAt).Select(m => m.Id).ToList();
        }

        throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
    }

    /// <summary>Returns null when the match fails a pathological-result guardrail (addendum §20).</summary>
    private static CompositeSearchResult? ToCompositeResult(
        PhoneticMatchSpan match,
        List<PhoneStreamEntry> candidateStream,
        IReadOnlyList<PhoneToken> queryTokens,
        int queryPhonemeCount,
        PhoneticSearchOptions options)
    {
        var indexedPhonemeEntries = Enumerable.Range(match.Start, match.End - match.Start)
            .Select(index => (Index: index, Entry: candidateStream[index]))
            .Where(x => !x.Entry.Token.IsBoundary)
            .ToList();

        if (indexedPhonemeEntries.Count == 0)
        {
            return null;
        }

        var componentGroups = GroupByMedia(indexedPhonemeEntries);

        if (componentGroups.Count > options.MaxSourceFiles)
        {
            return null;
        }

        if (componentGroups.Any(g => g.Count < options.MinPhonemesPerSource))
        {
            return null;
        }

        var components = componentGroups
            .Select(group => BuildComponent(group, match, candidateStream, queryTokens, options))
            .ToList();

        var overallScore = ScoreOf(match.Cost, queryPhonemeCount, options);

        var queryPhonemes = queryTokens.Where(t => !t.IsBoundary).Select(t => t.Symbol).ToList();
        return new CompositeSearchResult(overallScore, components, queryPhonemes);
    }

    private static List<List<(int Index, PhoneStreamEntry Entry)>> GroupByMedia(
        List<(int Index, PhoneStreamEntry Entry)> indexedPhonemeEntries)
    {
        var groups = new List<List<(int Index, PhoneStreamEntry Entry)>>();
        Guid? currentMediaId = null;
        List<(int Index, PhoneStreamEntry Entry)> currentGroup = [];

        foreach (var item in indexedPhonemeEntries)
        {
            if (item.Entry.MediaId != currentMediaId && currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
                currentGroup = [];
            }

            currentGroup.Add(item);
            currentMediaId = item.Entry.MediaId;
        }

        if (currentGroup.Count > 0)
        {
            groups.Add(currentGroup);
        }

        return groups;
    }

    private static CompositeMatchComponent BuildComponent(
        List<(int Index, PhoneStreamEntry Entry)> group,
        PhoneticMatchSpan match,
        List<PhoneStreamEntry> candidateStream,
        IReadOnlyList<PhoneToken> queryTokens,
        PhoneticSearchOptions options)
    {
        var mediaId = group[0].Entry.MediaId!.Value;
        var minIndex = group[0].Index;
        var maxIndex = group[^1].Index;

        // See PhoneticSearchService: null timing propagates instead of becoming a fake zero (#32).
        var startSeconds = group[0].Entry.StartSeconds;
        var endSeconds = group[^1].Entry.EndSeconds;

        var sourceText = PhoneStreamTextBuilder.BuildSourceText(group.Select(g => g.Entry));
        var ipa = PhoneStreamTextBuilder.BuildIpa(group.Select(g => g.Entry));
        var phonemes = group.Select(g => g.Entry.Token.Symbol).ToList();

        var correspondencesInRange = match.Correspondences
            .Where(c => c.CandidateIndex >= minIndex && c.CandidateIndex <= maxIndex)
            .ToList();

        int queryStart, queryEnd;
        if (correspondencesInRange.Count > 0)
        {
            queryStart = correspondencesInRange.Min(c => c.QueryIndex);
            queryEnd = correspondencesInRange.Max(c => c.QueryIndex) + 1;
        }
        else
        {
            // Purely inserted candidate phonemes with no direct query correspondence (rare) -
            // attribute them to an empty slice rather than guessing.
            queryStart = 0;
            queryEnd = 0;
        }

        var componentScore = correspondencesInRange.Count > 0
            ? ScoreOf(
                correspondencesInRange
                    .Select(c => PhonemeFeatureTable.SubstitutionCost(
                        queryTokens[c.QueryIndex].Symbol, candidateStream[c.CandidateIndex].Token.Symbol, options.SubstitutionMaxCost))
                    .Average(),
                1,
                options)
            : 0.0;

        return new CompositeMatchComponent(mediaId, startSeconds, endSeconds, sourceText, ipa, phonemes, componentScore, queryStart, queryEnd);
    }

    private static double ScoreOf(double cost, int normalizeBy, PhoneticSearchOptions options) =>
        normalizeBy > 0 && !double.IsPositiveInfinity(options.SubstitutionMaxCost)
            ? Math.Clamp(1 - cost / (normalizeBy * options.SubstitutionMaxCost), 0, 1)
            : cost == 0 ? 1.0 : 0.0;
}
