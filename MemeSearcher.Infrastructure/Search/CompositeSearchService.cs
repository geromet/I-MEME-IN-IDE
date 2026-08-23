using MemeSearcher.Core.Interfaces;
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
    IPhonemizer phonemizer) : ICompositeSearchService
{
    public async Task<IReadOnlyList<CompositeSearchResult>> SearchAsync(
        string queryText,
        string language,
        SearchScope scope,
        PhoneticSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= PhoneticSearchOptions.ForMode(SearchMode.SimilarPhonetic);

        var phonemizedQuery = await phonemizer.PhonemizeAsync(queryText, language, cancellationToken);
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
        var transcripts = await context.Transcripts
            .Where(t => mediaIds.Contains(t.MediaId))
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .ToListAsync(cancellationToken);

        var transcriptsByMedia = transcripts.ToLookup(t => t.MediaId);
        var candidateStream = PhoneStreamBuilder.BuildComposite(mediaIds.Select(id => transcriptsByMedia[id]));
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

        var startSeconds = group[0].Entry.StartSeconds!.Value;
        var endSeconds = group[^1].Entry.EndSeconds!.Value;

        var sourceText = string.Join(' ', DistinctConsecutiveWords(group.Select(g => g.Entry)));
        var ipa = string.Join(' ', GroupByWord(group.Select(g => g.Entry)));
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

        return new CompositeMatchComponent(mediaId, startSeconds, endSeconds, sourceText, ipa, phonemes, queryStart, queryEnd, componentScore);
    }

    private static double ScoreOf(double cost, int normalizeBy, PhoneticSearchOptions options) =>
        normalizeBy > 0 && !double.IsPositiveInfinity(options.SubstitutionMaxCost)
            ? Math.Clamp(1 - cost / (normalizeBy * options.SubstitutionMaxCost), 0, 1)
            : cost == 0 ? 1.0 : 0.0;

    private static IEnumerable<string> DistinctConsecutiveWords(IEnumerable<PhoneStreamEntry> phonemeEntries)
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

    private static IEnumerable<string> GroupByWord(IEnumerable<PhoneStreamEntry> phonemeEntries)
    {
        var currentWordId = (Guid?)null;
        var currentGroup = new List<string>();

        foreach (var entry in phonemeEntries)
        {
            if (entry.WordId != currentWordId && currentGroup.Count > 0)
            {
                yield return string.Concat(currentGroup);
                currentGroup = [];
            }

            currentGroup.Add(entry.Token.Symbol);
            currentWordId = entry.WordId;
        }

        if (currentGroup.Count > 0)
        {
            yield return string.Concat(currentGroup);
        }
    }
}
