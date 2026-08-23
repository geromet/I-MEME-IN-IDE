using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Infrastructure.Transcription;

/// <summary>
/// Plain text has no timestamp information (handoff §23). Each non-blank line becomes a cue with
/// null timing - not 0/0. The zero convention this class used to emit relied on every downstream
/// layer remembering to reinterpret it, and none did (#32); a null is enforced by the compiler.
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
            .Select(line => new ParsedCue(null, null, line))
            .ToList();

        return new ParsedTranscript(FormatName, cues);
    }
}
