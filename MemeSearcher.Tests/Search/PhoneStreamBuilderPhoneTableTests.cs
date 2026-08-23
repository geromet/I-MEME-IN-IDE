using MemeSearcher.Core.Models;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Search;

namespace MemeSearcher.Tests.Search;

/// <summary>
/// Covers the core regression in #18: the builder read only Word.PhonemeSequence and never touched
/// the Phone table, so everything phone-level alignment produced was inert for search.
/// </summary>
public class PhoneStreamBuilderPhoneTableTests
{
    private static Transcript MakeTranscript(params Word[] words)
    {
        var segmentId = Guid.NewGuid();
        foreach (var word in words)
        {
            word.SegmentId = segmentId;
        }

        return new Transcript
        {
            Id = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            Source = "srt",
            Language = "en-US",
            Segments =
            [
                new Segment
                {
                    Id = segmentId,
                    TranscriptId = Guid.NewGuid(),
                    Sequence = 0,
                    Text = string.Join(' ', words.Select(w => w.Text)),
                    StartSeconds = words.Min(w => w.StartSeconds),
                    EndSeconds = words.Max(w => w.EndSeconds),
                    Words = [.. words],
                },
            ],
        };
    }

    private static Word MakeWord(
        string text, string phonemeSequence, double start, double end,
        (string Symbol, double Start, double End)[]? phones = null,
        PhoneAlphabet phoneAlphabet = PhoneAlphabet.Arpabet)
    {
        var wordId = Guid.NewGuid();
        return new Word
        {
            Id = wordId,
            Sequence = 0,
            Text = text,
            PhonemeSequence = phonemeSequence,
            PhonemeAlphabet = PhoneAlphabet.Ipa,
            StartSeconds = start,
            EndSeconds = end,
            Phones = phones is null
                ? []
                : [.. phones.Select((p, i) => new Phone
                {
                    Id = Guid.NewGuid(),
                    WordId = wordId,
                    Sequence = i,
                    Symbol = p.Symbol,
                    Alphabet = phoneAlphabet,
                    StartSeconds = p.Start,
                    EndSeconds = p.End,
                })],
        };
    }

    private static List<string> Symbols(IEnumerable<PhoneStreamEntry> stream) =>
        stream.Where(e => !e.Token.IsBoundary).Select(e => e.Token.Symbol).ToList();

    /// <summary>
    /// The prediction and the alignment disagree on purpose - espeak predicted ə, the aligner
    /// reports EH1 (ɛ). Only the aligned vowel can come back if the Phone table is really being
    /// read, so a fallback cannot pass this.
    /// </summary>
    [Fact]
    public void Build_UsesPhoneRowsWhenAlignmentHasRun()
    {
        var word = MakeWord("hello", "h ə l oʊ", 1.0, 2.0,
            [("HH", 1.0, 1.1), ("EH1", 1.1, 1.3), ("L", 1.3, 1.5), ("OW1", 1.5, 2.0)]);

        var stream = PhoneStreamBuilder.Build(MakeTranscript(word));

        Assert.Equal(["h", "ɛ", "l", "oʊ"], Symbols(stream));
    }

    [Fact]
    public void Build_UsesPhoneLevelTimingRatherThanTheWholeWordSpan()
    {
        var word = MakeWord("hello", "h ə l oʊ", 1.0, 2.0,
            [("HH", 1.0, 1.1), ("AH0", 1.1, 1.3), ("L", 1.3, 1.5), ("OW1", 1.5, 2.0)]);

        var stream = PhoneStreamBuilder.Build(MakeTranscript(word)).Where(e => !e.Token.IsBoundary).ToList();

        // Before #18 every phoneme in a word shared that word's span; real per-phone timing is the
        // thing alignment produces and search could not previously see.
        Assert.Equal(1.0, stream[0].StartSeconds);
        Assert.Equal(1.1, stream[0].EndSeconds);
        Assert.Equal(1.5, stream[3].StartSeconds);
        Assert.Equal(2.0, stream[3].EndSeconds);
    }

    [Fact]
    public void Build_FallsBackToThePredictedSequenceWhenNoAlignmentHasRun()
    {
        // The ordinary state for an import - phone rows are only written by an explicit realign.
        var word = MakeWord("hello", "h ə l oʊ", 1.0, 2.0);

        var stream = PhoneStreamBuilder.Build(MakeTranscript(word));

        Assert.Equal(["h", "ə", "l", "oʊ"], Symbols(stream));
        Assert.All(stream.Where(e => !e.Token.IsBoundary), e =>
        {
            Assert.Equal(1.0, e.StartSeconds);
            Assert.Equal(2.0, e.EndSeconds);
        });
    }

    /// <summary>
    /// The round-trip the issue asks for: an ARPABET-aligned corpus reachable by an IPA query.
    /// The stored prediction is deliberately a different word, so passing this requires the
    /// ARPABET phones to have been read *and* converted.
    /// </summary>
    [Fact]
    public void Build_ConvertsArpabetPhonesSoAnIpaQueryCanMatchThem()
    {
        var word = MakeWord("judge", "dʒ ɑː dʒ", 0.0, 1.0,
            [("JH", 0.0, 0.3), ("AH1", 0.3, 0.6), ("JH", 0.6, 1.0)]);

        var candidate = PhoneStreamBuilder.Build(MakeTranscript(word));

        Assert.Equal(["dʒ", "ʌ", "dʒ"], Symbols(candidate));
    }

    [Fact]
    public void Build_DoesNotConvertPhonesThatAreAlreadyIpa()
    {
        var word = MakeWord("judge", "dʒ ʌ dʒ", 0.0, 1.0,
            [("dʒ", 0.0, 0.3), ("ʌ", 0.3, 0.6), ("dʒ", 0.6, 1.0)],
            phoneAlphabet: PhoneAlphabet.Ipa);

        Assert.Equal(["dʒ", "ʌ", "dʒ"], Symbols(PhoneStreamBuilder.Build(MakeTranscript(word))));
    }

    /// <summary>
    /// A word can hold both alphabets at once - IPA in PhonemeSequence from espeak, ARPABET in
    /// Phones from MFA. Each side must be read through its own tag, never through a guess.
    /// </summary>
    [Fact]
    public void Build_HandlesAWordCarryingBothAlphabetsSimultaneously()
    {
        var aligned = MakeWord("hello", "h ə l oʊ", 0.0, 1.0,
            [("HH", 0.0, 0.2), ("EH1", 0.2, 0.5), ("L", 0.5, 0.7), ("OW1", 0.7, 1.0)]);
        var unaligned = MakeWord("world", "w ɜː l d", 1.0, 2.0);
        unaligned.Sequence = 1;

        var stream = PhoneStreamBuilder.Build(MakeTranscript(aligned, unaligned));

        Assert.Equal(["h", "ɛ", "l", "oʊ", "w", "ɜː", "l", "d"], Symbols(stream));
    }
}
