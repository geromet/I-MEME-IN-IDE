using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Models;

public class Word
{
    public Guid Id { get; set; }
    public Guid SegmentId { get; set; }
    public int Sequence { get; set; }
    public required string Text { get; set; }
    public string? NormalizedText { get; set; }
    public string? Ipa { get; set; }
    public string? PhonemeSequence { get; set; }

    /// <summary>
    /// Which alphabet <see cref="Ipa"/> and <see cref="PhonemeSequence"/> are written in (#18).
    ///
    /// The tag has to live here rather than on Media or Transcript, because a single Word can hold
    /// two alphabets at once: espeak writes IPA into these fields while MFA writes ARPABET into
    /// <see cref="Phones"/>. Each carrier of phone data tags itself.
    /// </summary>
    public PhoneAlphabet PhonemeAlphabet { get; set; } = PhoneAlphabet.Ipa;
    /// <summary>Null when no timing is known for this word (#32) - the transcript had none and no
    /// alignment has placed it. Never a stand-in zero.</summary>
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }

    /// <summary>
    /// True when StartSeconds/EndSeconds is a character-proportional guess across the segment's own
    /// span (MediaIngestionService.BuildWordsFromInterpolation), not a real per-word timestamp from
    /// a transcription/alignment provider (#26). Meaningless when StartSeconds is null - there is no
    /// timing to qualify. The transcript viewer degrades to cue-level highlighting whenever this is
    /// true, since a plausible-looking guess is not the same guarantee real timing is.
    /// </summary>
    public bool IsTimingInterpolated { get; set; }

    public List<Phone> Phones { get; set; } = [];
}
