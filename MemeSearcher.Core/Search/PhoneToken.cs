namespace MemeSearcher.Core.Search;

/// <summary>
/// One element of a phoneme stream: either a phoneme symbol, or a word-boundary marker.
/// Boundaries are real tokens (not implicit gaps) so the matcher can charge a configurable -
/// by default cheap - cost for crossing them (handoff §12: "ice cream" vs "I scream" must be
/// comparable, so the boundary's *position* has to be something the matcher can shift for free).
/// </summary>
public readonly record struct PhoneToken
{
    public string Symbol { get; }
    public bool IsBoundary { get; }

    private PhoneToken(string symbol, bool isBoundary)
    {
        Symbol = symbol;
        IsBoundary = isBoundary;
    }

    public static PhoneToken Boundary { get; } = new(string.Empty, true);

    public static PhoneToken Phoneme(string symbol) => new(symbol, false);
}
