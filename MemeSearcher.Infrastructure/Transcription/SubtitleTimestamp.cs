using System.Globalization;

namespace MemeSearcher.Infrastructure.Transcription;

/// <summary>
/// Shared timestamp parsing for SRT ("00:00:01,000") and VTT ("00:00:01.000" / "01:00.000").
/// </summary>
internal static class SubtitleTimestamp
{
    public static double ParseSeconds(string raw)
    {
        var normalized = raw.Trim().Replace(',', '.');
        var parts = normalized.Split(':');

        double hours = 0, minutes, seconds;

        switch (parts.Length)
        {
            case 3:
                hours = double.Parse(parts[0], CultureInfo.InvariantCulture);
                minutes = double.Parse(parts[1], CultureInfo.InvariantCulture);
                seconds = double.Parse(parts[2], CultureInfo.InvariantCulture);
                break;
            case 2:
                minutes = double.Parse(parts[0], CultureInfo.InvariantCulture);
                seconds = double.Parse(parts[1], CultureInfo.InvariantCulture);
                break;
            default:
                throw new FormatException($"Unrecognized timestamp: '{raw}'");
        }

        return hours * 3600 + minutes * 60 + seconds;
    }
}
