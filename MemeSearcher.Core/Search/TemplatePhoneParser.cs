using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Search;

/// <summary>
/// Turns a TemplateVariant's raw authored phones into the same PhoneToken shape the matcher
/// already consumes from text queries (PhoneStreamBuilder.BuildQueryTokens) and from the corpus
/// (PhoneStreamBuilder.Build) - the seam #21 relies on to bypass the phonemizer entirely.
/// </summary>
public static class TemplatePhoneParser
{
    public readonly record struct ParsedSymbol(string AsAuthored, string Canonical, bool IsKnown);

    /// <summary>
    /// Splits on "|" for word-boundary groups and whitespace for individual phones, converts each
    /// symbol from <paramref name="alphabet"/> to canonical IPA (#18 - the alphabet the matcher and
    /// PhonemeFeatureTable assume), and reports which symbols PhonemeFeatureTable doesn't recognise
    /// so an editor can flag them rather than silently building a query that can never match.
    /// </summary>
    public static IReadOnlyList<ParsedSymbol> ParseSymbols(string phonesRaw, PhoneAlphabet alphabet)
    {
        var symbols = phonesRaw
            .Split(['|', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != "|")
            .ToList();

        return symbols
            .Select(s =>
            {
                var canonical = PhoneAlphabetConverter.ToCanonical(s, alphabet).Symbol;
                var isKnown = canonical.Length > 0 && PhonemeFeatureTable.TryGetFeature(canonical, out _);
                return new ParsedSymbol(s, canonical, isKnown);
            })
            .ToList();
    }

    /// <summary>Builds the matcher-ready token stream: canonical phones with a PhoneToken.Boundary between "|"-separated groups. Unknown symbols still produce a token - validation is the editor's job (see ParseSymbols), not this method's.</summary>
    public static IReadOnlyList<PhoneToken> BuildTokens(string phonesRaw, PhoneAlphabet alphabet)
    {
        var groups = phonesRaw.Split('|');
        var tokens = new List<PhoneToken>();

        for (var g = 0; g < groups.Length; g++)
        {
            var symbols = groups[g].Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (symbols.Length == 0)
            {
                continue;
            }

            if (tokens.Count > 0)
            {
                tokens.Add(PhoneToken.Boundary);
            }

            tokens.AddRange(PhoneAlphabetConverter.ToCanonical(symbols, alphabet)
                .Where(c => c.Symbol.Length > 0)
                .Select(c => PhoneToken.Phoneme(c.Symbol)));
        }

        return tokens;
    }
}
