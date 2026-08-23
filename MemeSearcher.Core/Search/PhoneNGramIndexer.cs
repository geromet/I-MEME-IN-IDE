namespace MemeSearcher.Core.Search;

/// <summary>
/// Trigram key extraction shared by index population and query-side candidate generation (#9), so
/// both sides of a lookup agree on what a "phoneme n-gram" is.
///
/// Built over the phoneme-only subsequence of a token stream: boundaries carry no symbol, so an
/// n-gram spans across a word/segment break the same way a listener glides across one, and a
/// boundary can never itself be part of an n-gram.
/// </summary>
public static class PhoneNGramIndexer
{
    /// <summary>
    /// Trigrams: long enough to be selective (a single stray trigram hit rarely spans an entire
    /// media item), short enough that one substitution near either edge of a query still leaves at
    /// least one exact-or-near trigram elsewhere in the query to seed a window from - the recall
    /// property #9 requires of this filter.
    /// </summary>
    public const int Length = 3;

    /// <summary>Joins n-gram symbols. Not a valid IPA symbol, so it can never collide with a real phoneme.</summary>
    private const char Separator = '';

    /// <summary>One n-gram and the position (into the token list it was extracted from) of its first phoneme.</summary>
    public readonly record struct Occurrence(string NGram, int Position);

    /// <summary>
    /// Extracts every length-<see cref="Length"/> n-gram from <paramref name="tokens"/>, paired
    /// with the index - into <paramref name="tokens"/> itself - of its first phoneme. That is the
    /// same coordinate space <see cref="PhoneticSequenceMatcher"/> uses for match Start/End, so a
    /// posting's position can seed a matcher window directly, with no conversion step to get
    /// wrong.
    /// </summary>
    public static List<Occurrence> Extract(IReadOnlyList<PhoneToken> tokens)
    {
        var phonemePositions = new List<int>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].IsBoundary)
            {
                phonemePositions.Add(i);
            }
        }

        var occurrences = new List<Occurrence>();
        for (var i = 0; i + Length <= phonemePositions.Count; i++)
        {
            var symbols = new string[Length];
            for (var k = 0; k < Length; k++)
            {
                symbols[k] = tokens[phonemePositions[i + k]].Symbol;
            }

            occurrences.Add(new Occurrence(Join(symbols), phonemePositions[i]));
        }

        return occurrences;
    }

    /// <summary>Symbols making up an n-gram key, in order - the inverse of <see cref="Join"/>. Used by fuzzy expansion to vary one position at a time.</summary>
    public static string[] Split(string ngram) => ngram.Split(Separator);

    public static string Join(IReadOnlyList<string> symbols) => string.Join(Separator, symbols);
}
