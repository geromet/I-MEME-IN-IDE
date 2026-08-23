using MemeSearcher.Core.Transcripts;

namespace MemeSearcher.Core.Interfaces;

public interface ITranscriptParser
{
    string FormatName { get; }

    bool CanParse(string filePath);

    ParsedTranscript Parse(string content);
}
