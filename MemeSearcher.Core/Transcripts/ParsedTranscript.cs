namespace MemeSearcher.Core.Transcripts;

/// <summary>Real measured timing for one word, when it's available (Milestone 5) - see TranscribedWord for the same shape on the ITranscriptionProvider side.</summary>
public record ParsedWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>
/// Words is null for the ordinary SRT/VTT/plain-text parsers (they have no per-word timing to
/// give) and for the media-transcribed-directly path when the provider didn't produce word-level
/// alignment. When non-null, MediaIngestionService uses it instead of the character-proportional
/// interpolation placeholder (handoff §49/50: predicted vs actual pronunciation/timing).
/// </summary>
public record ParsedCue(double StartSeconds, double EndSeconds, string Text, IReadOnlyList<ParsedWord>? Words = null);

public record ParsedTranscript(string SourceFormat, IReadOnlyList<ParsedCue> Cues);
