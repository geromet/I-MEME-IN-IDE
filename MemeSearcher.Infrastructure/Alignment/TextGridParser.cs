using System.Globalization;
using System.Text.RegularExpressions;

namespace MemeSearcher.Infrastructure.Alignment;

/// <summary>
/// Parses Praat's "long text" TextGrid format, which MFA outputs directly (a stable, extensively
/// documented format - MFA doesn't invent its own). Built against the documented format spec
/// rather than a real MFA output sample, since MFA isn't installed in this environment; the
/// structure (item blocks each with a "name", each containing numbered "intervals" with
/// xmin/xmax/text) is well established and not expected to drift.
/// </summary>
public static partial class TextGridParser
{
    public record Interval(double StartSeconds, double EndSeconds, string Text);

    [GeneratedRegex(@"^intervals\s*\[\d+\]:$")]
    private static partial Regex IntervalMarker();

    /// <summary>Returns tier name -> ordered intervals (including empty-text/silence intervals - callers filter as needed).</summary>
    public static IReadOnlyDictionary<string, List<Interval>> Parse(string textGridContent)
    {
        var tiers = new Dictionary<string, List<Interval>>();

        string? currentTierName = null;
        var inInterval = false;
        double? pendingStart = null;
        double? pendingEnd = null;

        foreach (var rawLine in textGridContent.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("name = "))
            {
                currentTierName = ExtractQuoted(line);
                if (currentTierName is not null && !tiers.ContainsKey(currentTierName))
                {
                    tiers[currentTierName] = [];
                }

                inInterval = false;
                continue;
            }

            if (IntervalMarker().IsMatch(line))
            {
                inInterval = true;
                pendingStart = null;
                pendingEnd = null;
                continue;
            }

            if (!inInterval || currentTierName is null)
            {
                continue;
            }

            if (line.StartsWith("xmin = ") && pendingStart is null)
            {
                pendingStart = ParseDouble(line);
            }
            else if (line.StartsWith("xmax = ") && pendingEnd is null)
            {
                pendingEnd = ParseDouble(line);
            }
            else if (line.StartsWith("text = ") && pendingStart is not null && pendingEnd is not null)
            {
                tiers[currentTierName].Add(new Interval(pendingStart.Value, pendingEnd.Value, ExtractQuoted(line) ?? ""));
                inInterval = false;
            }
        }

        return tiers;
    }

    private static string? ExtractQuoted(string line)
    {
        var firstQuote = line.IndexOf('"');
        var lastQuote = line.LastIndexOf('"');
        return firstQuote >= 0 && lastQuote > firstQuote
            ? line[(firstQuote + 1)..lastQuote]
            : null;
    }

    private static double ParseDouble(string line)
    {
        var value = line[(line.IndexOf('=') + 1)..].Trim();
        return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
