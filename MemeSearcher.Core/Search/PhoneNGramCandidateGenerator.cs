using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Search;

/// <summary>
/// Recall-oriented candidate window generation (#9): narrows PhoneticSequenceMatcher's O(query *
/// candidate) DP down to windows around n-gram hits, instead of the full candidate stream. This is
/// a filter in front of the matcher, not a second scorer - the DP re-runs unchanged inside each
/// window, so a match found inside a window scores exactly what full-stream search would have
/// produced for a match starting there. The only way this can lose recall is by failing to open a
/// window somewhere a true match starts, so callers must measure recall loss on real data
/// (issue #9's exit criteria), not just speed.
/// </summary>
public static class PhoneNGramCandidateGenerator
{
    /// <summary>One padded, merged region of the candidate stream worth running the matcher over.</summary>
    public readonly record struct Window(int Start, int End)
    {
        public int Length => End - Start;
    }

    /// <summary>
    /// Query n-grams, expanded with phonetically-similar variants so a query that differs from the
    /// stored corpus by one near-miss phoneme still lands on an indexed trigram somewhere.
    ///
    /// Only one position is varied at a time (not full combinatorial expansion across all three) -
    /// deliberately, and this is a measured, known recall gap, not an oversight. A budget generous
    /// enough to reach cross-class substitutions (see below) makes NearbySymbols return most of the
    /// same-class alphabet - 72 of ~85 known symbols measured for a "water"-sized query's budget -
    /// so two-position expansion would multiply that by itself per trigram and degenerate the index
    /// into little more than a unigram lookup on the one fixed position, destroying the selectivity
    /// (9-14% of a media's stream scanned, measured on #8's synthetic corpus) that is this
    /// milestone's entire point. CandidateGenerationRecallTests measures the resulting gap directly:
    /// a query whose *only* indexed trigram differs from every occurrence in the corpus by two
    /// positions at once (not one) is missed - observed for one 4-phone single-word query
    /// ("water" vs "mother"/"small", both needing two simultaneous substitutions) under
    /// SimilarPhonetic/FuzzyPhonetic (3 of 25 results) and LoosePhonetic (1 of 10); a longer query
    /// with more trigrams has more chances at least one is clean, and none of the tested longer
    /// queries lost anything. That gap is accepted and pinned by the recall test, not silently
    /// dropped - see the test file for the exact counts.
    ///
    /// Expands up to the *whole match's* accepted-cost budget
    /// (<see cref="PhoneticSequenceMatcher.MaxAcceptableCost"/> for <paramref name="queryLength"/>),
    /// not <see cref="PhoneticSearchOptions.SubstitutionMaxCost"/> itself. A single substitution
    /// costing more than <c>SubstitutionMaxCost</c> is still real: cross-class (vowel/consonant)
    /// substitutions cost <c>CrossClassMultiplier * SubstitutionMaxCost</c> (1.25x), and the
    /// matcher can still accept an overall match that spends most of its budget on exactly one such
    /// substitution as long as every other position is cheap. Excluding those from expansion
    /// measurably lost real matches on #8's synthetic corpus in testing (see #9's recorded recall
    /// numbers) - a short query especially has few trigrams to fall back on, so even one
    /// wrongly-excluded substitution can sink the whole query. This costs selectivity - reported by
    /// SearchBenchmarks/CandidateGenerationRecallTests rather than assumed - but #9 is explicit that
    /// a speedup which silently drops matches is a regression, not an improvement, and a
    /// selectivity loss is not.
    /// </summary>
    public static HashSet<string> ExpandFuzzy(IEnumerable<string> exactNGrams, PhoneticSearchOptions options, int queryLength)
    {
        var expanded = new HashSet<string>(exactNGrams);

        var budget = PhoneticSequenceMatcher.MaxAcceptableCost(queryLength, options);

        // Zero or infinite budget (ExactPhonetic mode, or a caller that disabled substitutions
        // outright) means no substitution is ever cheap enough to accept, so there is nothing
        // fuzzy to expand into.
        if (budget <= 0 || double.IsPositiveInfinity(budget))
        {
            return expanded;
        }

        foreach (var ngram in exactNGrams)
        {
            var symbols = PhoneNGramIndexer.Split(ngram);

            for (var position = 0; position < symbols.Length; position++)
            {
                foreach (var neighbor in PhonemeFeatureTable.NearbySymbols(symbols[position], budget))
                {
                    var variant = (string[])symbols.Clone();
                    variant[position] = neighbor;
                    expanded.Add(PhoneNGramIndexer.Join(variant));
                }
            }
        }

        return expanded;
    }

    /// <summary>
    /// A word/segment boundary crossing costs WordBoundaryCost, not InsertionCost - but boundaries
    /// are structurally sparse (PhoneStreamBuilder emits exactly one between two non-empty words,
    /// never several in a row), so a small fixed allowance for edge boundary-hopping is safe
    /// without needing WordBoundaryCost in the budget division below - dividing by a cost that can
    /// be 0 (several SearchMode presets use WordBoundaryCost = 0.05-0.1, and ExactPhonetic uses 0)
    /// would either explode or wrongly report "unbounded".
    /// </summary>
    private const int BoundaryCrossingSlack = 4;

    /// <summary>
    /// A safe (recall-oriented) padding bound for <see cref="GenerateWindows"/>: how far past
    /// <paramref name="queryLength"/> a match could plausibly extend into the candidate stream,
    /// given <paramref name="options"/>' own cost model - not a guess.
    ///
    /// Only PhoneticSequenceMatcher's "delete" move (candidate has a phoneme the query doesn't)
    /// extends how far into the candidate stream a match reaches; the symmetric "insert" move
    /// consumes only the query side and never moves the candidate pointer. That delete move costs
    /// <see cref="PhoneticSearchOptions.InsertionCost"/> per real phoneme, so - together with
    /// <see cref="BoundaryCrossingSlack"/> for the edges' boundary tokens - that is the only cost
    /// this needs to reason about; DeletionCost and CrossFileTransitionCost genuinely do not apply
    /// here (single-file search never sees a cross-file boundary).
    ///
    /// Returns null when the extension cannot be bounded at all - a free (cost 0) real-phoneme
    /// insertion, or an unbounded accepted-cost budget paired with a finite insertion cost -
    /// meaning callers must fall back to full-stream search rather than pick an arbitrary number.
    /// </summary>
    public static int? SafePadding(int queryLength, PhoneticSearchOptions options)
    {
        if (options.InsertionCost == 0)
        {
            return null;
        }

        if (double.IsPositiveInfinity(options.InsertionCost))
        {
            // No real phoneme can ever be inserted on the candidate side (e.g. ExactPhonetic), so a
            // match can only extend past the query's own length via free boundary hops at its edges.
            return queryLength + BoundaryCrossingSlack;
        }

        var budget = PhoneticSequenceMatcher.MaxAcceptableCost(queryLength, options);
        if (double.IsPositiveInfinity(budget))
        {
            return null;
        }

        var extraSlack = (int)Math.Ceiling(budget / options.InsertionCost);
        return queryLength + extraSlack + BoundaryCrossingSlack;
    }

    /// <summary>
    /// Looks up every n-gram in <paramref name="ngrams"/> via <paramref name="lookupPostings"/>,
    /// pads each hit by <paramref name="padding"/> positions on both sides - a true match can start
    /// or end some distance from the n-gram that seeded it, since the seeding n-gram may fall
    /// anywhere inside the eventual match rather than exactly at its edge - and merges
    /// overlapping/adjacent windows so the matcher never scores the same region twice.
    ///
    /// Returns null, not an empty list, when <paramref name="ngrams"/> is empty: a query too short
    /// to form even one n-gram cannot be filtered by this technique at all, and narrowing anyway
    /// would be a coin flip rather than a recall-oriented decision. Callers must treat null as "run
    /// the matcher over the whole candidate stream instead" - distinct from an empty window list,
    /// which means "looked, and found no candidate anywhere".
    /// </summary>
    public static List<Window>? GenerateWindows(
        IReadOnlyCollection<string> ngrams,
        Func<string, IReadOnlyList<int>> lookupPostings,
        int candidateLength,
        int padding)
    {
        if (ngrams.Count == 0)
        {
            return null;
        }

        var hits = new SortedSet<int>();
        foreach (var ngram in ngrams)
        {
            foreach (var position in lookupPostings(ngram))
            {
                hits.Add(position);
            }
        }

        var windows = new List<Window>();
        foreach (var hit in hits)
        {
            var start = Math.Max(0, hit - padding);
            var end = Math.Min(candidateLength, hit + padding + 1);

            if (windows.Count > 0 && start <= windows[^1].End)
            {
                windows[^1] = new Window(windows[^1].Start, Math.Max(windows[^1].End, end));
            }
            else
            {
                windows.Add(new Window(start, end));
            }
        }

        return windows;
    }
}
