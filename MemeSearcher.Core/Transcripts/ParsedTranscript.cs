namespace MemeSearcher.Core.Transcripts;

public record ParsedCue(double StartSeconds, double EndSeconds, string Text);

public record ParsedTranscript(string SourceFormat, IReadOnlyList<ParsedCue> Cues);
