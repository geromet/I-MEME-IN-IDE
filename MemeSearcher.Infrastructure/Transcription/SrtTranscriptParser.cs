using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Infrastructure.Transcription;

public class SrtTranscriptParser : ITranscriptParser
{
    public string FormatName => "srt";

    public bool CanParse(string filePath) =>
        filePath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase);

    public ParsedTranscript Parse(string content)
    {
        var cues = new List<ParsedCue>();

        foreach (var block in SplitIntoBlocks(content))
        {
            var lines = block;

            // Optional numeric cue index line.
            var start = 0;
            if (start < lines.Count && int.TryParse(lines[start].Trim(), out _))
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
        return (SubtitleTimestamp.ParseSeconds(parts[0]), SubtitleTimestamp.ParseSeconds(parts[1].Trim().Split(' ')[0]));
    }
}
