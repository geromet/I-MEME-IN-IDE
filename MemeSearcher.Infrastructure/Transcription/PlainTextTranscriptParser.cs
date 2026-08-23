using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Infrastructure.Transcription;

/// <summary>
/// Plain text has no timestamp information (handoff §23). Each non-blank line becomes a cue
/// with Start == End == 0, a convention the UI/search layer must treat as "timing unknown" rather
/// than a literal instant.
/// </summary>
public class PlainTextTranscriptParser : ITranscriptParser
{
    public string FormatName => "text";

    public bool CanParse(string filePath) =>
        filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    public ParsedTranscript Parse(string content)
    {
        var cues = content
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => new ParsedCue(0, 0, line))
            .ToList();

        return new ParsedTranscript(FormatName, cues);
    }
}
