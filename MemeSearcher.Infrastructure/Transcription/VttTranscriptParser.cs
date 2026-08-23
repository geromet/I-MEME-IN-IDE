using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Infrastructure.Transcription;

public class VttTranscriptParser : ITranscriptParser
{
    public string FormatName => "vtt";

    public bool CanParse(string filePath) =>
        filePath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase);

    public ParsedTranscript Parse(string content)
    {
        var cues = new List<ParsedCue>();

        foreach (var block in SplitIntoBlocks(content))
        {
            var lines = block;

            // Skip the "WEBVTT" header block (and any leading metadata lines within it).
            if (lines[0].TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Optional cue identifier line before the timestamp line.
            var start = 0;
            if (start < lines.Count && !lines[start].Contains("-->"))
            {
                start++;
            }

            if (start >= lines.Count || !lines[start].Contains("-->"))
            {
                continue;
            }

            var (startSeconds, endSeconds) = ParseTimeRange(lines[start]);
            var text = string.Join(' ', lines.Skip(start + 1).Select(l => l.Trim())).Trim();

            if (text.Length > 0)
            {
                cues.Add(new ParsedCue(startSeconds, endSeconds, text));
            }
        }

        return new ParsedTranscript(FormatName, cues);
    }

    private static IEnumerable<List<string>> SplitIntoBlocks(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0)
                {
                    yield return current;
                    current = [];
                }

                continue;
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static (double Start, double End) ParseTimeRange(string line)
    {
        var parts = line.Split("-->", 2);
        var endToken = parts[1].Trim().Split(' ')[0];
        return (SubtitleTimestamp.ParseSeconds(parts[0]), SubtitleTimestamp.ParseSeconds(endToken));
    }
}
