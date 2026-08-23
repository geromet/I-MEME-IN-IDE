namespace MemeSearcher.Core.Phonetics;

/// <summary>
/// The ARPABET phone set, split by whether a symbol can carry a stress digit. Used both by the
/// detector (is this token even ARPABET?) and by the converter.
/// </summary>
public static class ArpabetInventory
{
    /// <summary>Vowels - the only ARPABET symbols that take a trailing stress digit (0/1/2).</summary>
    public static IReadOnlySet<string> Vowels { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AA", "AE", "AH", "AO", "AW", "AY", "EH", "ER", "EY", "IH", "IY", "OW", "OY", "UH", "UW",
    };

    public static IReadOnlySet<string> Consonants { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "B", "CH", "D", "DH", "F", "G", "HH", "JH", "K", "L", "M", "N", "NG", "P", "R", "S", "SH",
        "T", "TH", "V", "W", "Y", "Z", "ZH",
    };

    public static bool Contains(string baseSymbol) =>
        Vowels.Contains(baseSymbol) || Consonants.Contains(baseSymbol);

    /// <summary>
    /// Splits a token into its base symbol and stress digit. "AH0" -> ("AH", 0); "HH" -> ("HH",
    /// null). Returns false for anything that is not shaped like an ARPABET token at all.
    /// </summary>
    public static bool TrySplit(string token, out string baseSymbol, out int? stress)
    {
        baseSymbol = token;
        stress = null;

        if (token.Length == 0)
        {
            return false;
        }

        var last = token[^1];
        if (last is >= '0' and <= '2')
        {
            baseSymbol = token[..^1];
            stress = last - '0';
        }

        return baseSymbol.Length is >= 1 and <= 2 && baseSymbol.All(char.IsAsciiLetter);
    }
}
