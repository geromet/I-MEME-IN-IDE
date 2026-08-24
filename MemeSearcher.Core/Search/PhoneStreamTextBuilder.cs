namespace MemeSearcher.Core.Search;

/// <summary>
/// Turns a matched span of the phone stream back into human-readable text (#17): the distinct
/// words it covers, and each word's phones concatenated together. PhoneticSearchService and
/// CompositeSearchService had each grown their own copy of this - identical for
/// DistinctConsecutiveWords, and drifted in shape (though not rendered output) for the word-phone
/// grouping: one joined groups-of-symbols with string.Concat at the call site, the other did the
/// concat inside the helper. This is the one canonical version both now call.
/// </summary>
public static class PhoneStreamTextBuilder
{
    /// <summary>The transcript words a matched span covers, in order, collapsed to one entry per word regardless of how many phones that word contributed.</summary>
    public static IEnumerable<string> DistinctConsecutiveWords(IEnumerable<PhoneStreamEntry> phonemeEntries)
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

    /// <summary>Each word's phone symbols concatenated together, one string per word, in order.</summary>
    public static IEnumerable<string> GroupByWord(IEnumerable<PhoneStreamEntry> phonemeEntries)
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

    public static string BuildSourceText(IEnumerable<PhoneStreamEntry> phonemeEntries) =>
        string.Join(' ', DistinctConsecutiveWords(phonemeEntries));

    public static string BuildIpa(IEnumerable<PhoneStreamEntry> phonemeEntries) =>
        string.Join(' ', GroupByWord(phonemeEntries));
}
