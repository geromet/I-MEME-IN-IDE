namespace MemeSearcher.Core.Search;

/// <summary>
/// One element of a phoneme stream: a phoneme symbol, an intra-file word-boundary marker, or
/// (Milestone 4) a cross-file transition marker. Boundaries are real tokens (not implicit gaps)
/// so the matcher can charge a configurable cost for crossing them (handoff §12: "ice cream" vs
/// "I scream" must be comparable, so the boundary's *position* has to be something the matcher
/// can shift for free). Cross-file boundaries are a distinct, separately-priced kind of boundary
/// (addendum §19) - same-recording transitions stay cheap, cross-recording ones cost more, so
/// composite results don't form gratuitously.
/// </summary>
public readonly record struct PhoneToken
{
    public string Symbol { get; }
    public bool IsBoundary { get; }
    public bool IsCrossFileBoundary { get; }

    private PhoneToken(string symbol, bool isBoundary, bool isCrossFileBoundary)
    {
        Symbol = symbol;
        IsBoundary = isBoundary;
        IsCrossFileBoundary = isCrossFileBoundary;
    }

    public static PhoneToken Boundary { get; } = new(string.Empty, true, false);

    public static PhoneToken CrossFileBoundary { get; } = new(string.Empty, true, true);

    public static PhoneToken Phoneme(string symbol) => new(symbol, false, false);
}
