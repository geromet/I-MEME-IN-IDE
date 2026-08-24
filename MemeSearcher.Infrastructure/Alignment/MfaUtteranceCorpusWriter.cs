using System.Globalization;
using System.Text;
using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Alignment;

/// <summary>
/// Writes MFA's TextGrid-corpus input format (#33): a single interval tier of utterance spans
/// against the *intact* audio file, rather than one `.lab` covering the whole recording as one
/// utterance. Chosen over cutting the audio into per-utterance clips because it preserves the
/// original file and the timings already stored in the transcript, and matches how MFA expects a
/// corpus to look (Praat's own "long text" TextGrid format, which MFA reads and writes directly).
///
/// Built against the documented Praat format rather than a real MFA sample, same as
/// <see cref="TextGridParser"/> (mfa is not installed in this environment) - round-tripped through
/// that same parser in tests instead, so both sides of the format are exercised against each other.
/// </summary>
public static class MfaUtteranceCorpusWriter
{
    private const string TierName = "utterances";

    /// <summary>
    /// Builds one contiguous, gap-filled interval tier covering [0, totalDurationSeconds]: real
    /// utterances where given, empty-text silence intervals everywhere else - Praat/MFA's TextGrid
    /// format requires the tier's intervals to tile the whole span with no gaps or overlaps.
    /// Utterances are sorted by start time and clamped to the tier's bounds and to each other, so a
    /// segment with slightly inconsistent timing (out of order, touching its neighbour) can't
    /// produce an invalid (zero/negative-length, or overlapping) interval.
    /// </summary>
    public static string Write(IReadOnlyList<AlignmentUtterance> utterances, double totalDurationSeconds)
    {
        if (totalDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalDurationSeconds), totalDurationSeconds, "Total duration must be known and positive to build a TextGrid corpus.");
        }

        var intervals = BuildIntervals(utterances, totalDurationSeconds);

        var sb = new StringBuilder();
        sb.AppendLine("File type = \"ooTextFile\"");
        sb.AppendLine("Object class = \"TextGrid\"");
        sb.AppendLine();
        sb.AppendLine("xmin = 0");
        sb.AppendLine(FormattableLine("xmax", totalDurationSeconds));
        sb.AppendLine("tiers? <exists>");
        sb.AppendLine("size = 1");
        sb.AppendLine("item []:");
        sb.AppendLine("    item [1]:");
        sb.AppendLine("        class = \"IntervalTier\"");
        sb.AppendLine($"        name = \"{EscapeQuotes(TierName)}\"");
        sb.AppendLine("        xmin = 0");
        sb.AppendLine(FormattableLine("        xmax", totalDurationSeconds));
        sb.AppendLine($"        intervals: size = {intervals.Count}");

        for (var i = 0; i < intervals.Count; i++)
        {
            var (start, end, text) = intervals[i];
            sb.AppendLine($"        intervals [{i + 1}]:");
            sb.AppendLine(FormattableLine("            xmin", start));
            sb.AppendLine(FormattableLine("            xmax", end));
            sb.AppendLine($"            text = \"{EscapeQuotes(text)}\"");
        }

        return sb.ToString();
    }

    private static List<(double Start, double End, string Text)> BuildIntervals(
        IReadOnlyList<AlignmentUtterance> utterances, double totalDurationSeconds)
    {
        var intervals = new List<(double Start, double End, string Text)>();
        var cursor = 0.0;

        foreach (var utterance in utterances.OrderBy(u => u.StartSeconds))
        {
            var start = Math.Clamp(utterance.StartSeconds, cursor, totalDurationSeconds);
            var end = Math.Clamp(utterance.EndSeconds, start, totalDurationSeconds);

            if (end <= start)
            {
                // Degenerate after clamping (e.g. entirely overlapped by a prior utterance) - a
                // zero/negative-length interval would corrupt the tier's tiling, so this
                // utterance is dropped rather than emitted. Its text simply isn't aligned - #32's
                // "do not fabricate" rule applies to positions as much as to timestamps.
                continue;
            }

            if (start > cursor)
            {
                intervals.Add((cursor, start, ""));
            }

            intervals.Add((start, end, utterance.Text));
            cursor = end;
        }

        if (cursor < totalDurationSeconds)
        {
            intervals.Add((cursor, totalDurationSeconds, ""));
        }

        return intervals;
    }

    private static string FormattableLine(string key, double value) =>
        $"{key} = {value.ToString("0.######", CultureInfo.InvariantCulture)}";

    /// <summary>Praat's quoted-string escaping: an embedded double quote is written doubled, same as Pascal string literals.</summary>
    private static string EscapeQuotes(string text) => text.Replace("\"", "\"\"");
}
