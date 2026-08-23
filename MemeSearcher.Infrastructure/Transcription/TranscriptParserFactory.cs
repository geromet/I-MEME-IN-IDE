using MemeSearcher.Core.Interfaces;

namespace MemeSearcher.Infrastructure.Transcription;

public class TranscriptParserFactory(IEnumerable<ITranscriptParser> parsers)
{
    public static TranscriptParserFactory CreateDefault() => new([
        new SrtTranscriptParser(),
        new VttTranscriptParser(),
        new PlainTextTranscriptParser(),
    ]);

    public ITranscriptParser GetParser(string filePath) =>
        parsers.FirstOrDefault(p => p.CanParse(filePath))
        ?? throw new NotSupportedException($"No transcript parser registered for '{filePath}'.");
}
