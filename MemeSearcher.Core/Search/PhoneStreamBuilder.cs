using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Phonetics;

namespace MemeSearcher.Core.Search;

/// <summary>
/// Builds the continuous phoneme stream for one media item's transcript (handoff §16/§17): all
/// segments and words in order, phonemes flattened into a single sequence, with a low-cost-to-cross
/// boundary token between every pair of words - including across segment/subtitle-cue boundaries,
/// since the docs are explicit that matches must be able to begin/end mid-segment.
/// </summary>
public static class PhoneStreamBuilder
{
    public static List<PhoneStreamEntry> Build(Transcript transcript) => Build([transcript]);

    /// <summary>
    /// Multiple transcripts for the same media (e.g. reprocessed sources - addendum §5) are
    /// concatenated into one stream, with the same low-cost boundary between them as between words.
    /// </summary>
    public static List<PhoneStreamEntry> Build(IEnumerable<Transcript> transcripts)
    {
        var stream = new List<PhoneStreamEntry>();
        AppendTranscripts(stream, transcripts, isFirstWord: true);
        return stream;
    }

    /// <summary>
    /// Milestone 4: concatenates multiple *different* media's streams into one combined stream for
    /// composite search, so the existing single-pass matcher can discover matches that span source
    /// files. The seam between two media items is a CrossFileBoundary, not a plain word boundary -
    /// separately priced (addendum §19) so same-file continuity stays cheap and cross-file jumps
    /// cost more by default. Media order is the order given here; the matcher can only stitch
    /// files together in that fixed order, not any arbitrary order - a true corpus graph allowing
    /// any-to-any transitions is a documented follow-up, not implemented here.
    /// </summary>
    public static List<PhoneStreamEntry> BuildComposite(IEnumerable<IEnumerable<Transcript>> mediaTranscriptGroups)
    {
        var stream = new List<PhoneStreamEntry>();
        var isFirstGroup = true;

        foreach (var transcripts in mediaTranscriptGroups)
        {
            if (!isFirstGroup && stream.Count > 0)
            {
                stream.Add(PhoneStreamEntry.CrossFileBoundary());
            }

            AppendTranscripts(stream, transcripts, isFirstWord: true);
            isFirstGroup = false;
        }

        return stream;
    }

    private static void AppendTranscripts(List<PhoneStreamEntry> stream, IEnumerable<Transcript> transcripts, bool isFirstWord)
    {
        foreach (var transcript in transcripts.OrderBy(t => t.CreatedAt))
        {
            foreach (var segment in transcript.Segments.OrderBy(s => s.Sequence))
            {
                foreach (var word in segment.Words.OrderBy(w => w.Sequence))
                {
                    var phones = BuildWordPhones(word);
                    if (phones.Count == 0)
                    {
                        continue;
                    }

                    if (!isFirstWord)
                    {
                        stream.Add(PhoneStreamEntry.Boundary());
                    }

                    foreach (var phone in phones)
                    {
                        stream.Add(PhoneStreamEntry.Phoneme(
                            phone.Symbol, transcript.MediaId, segment.Id, word.Id, word.Text,
                            phone.StartSeconds ?? word.StartSeconds,
                            phone.EndSeconds ?? word.EndSeconds,
                            phone.IsPhoneLevelAligned));
                    }

                    isFirstWord = false;
                }
            }
        }
    }

    /// <summary>
    /// One word's contribution to the stream, in canonical (IPA) symbols (#18).
    ///
    /// Prefers the <c>Phone</c> rows when they exist. Until this was written, the builder read
    /// only <c>Word.PhonemeSequence</c> and never touched the Phone table at all - so everything
    /// phone-level alignment produced was inert for search: real per-phone symbols and timings
    /// that no query could ever match against. This is what makes searching the *actual*
    /// pronunciation real rather than architectural (handoff §49: do not pretend eSpeak output is
    /// an acoustic transcription of the speaker). Before, the app stored the actual and searched
    /// only the prediction.
    ///
    /// Falls back to the phonemizer's predicted sequence where no alignment has run, which is the
    /// normal state for an ordinary import - phone rows are populated only by an explicit
    /// realignment.
    ///
    /// Both paths convert through the stored alphabet tag, so an ARPABET-aligned corpus is
    /// searchable by an IPA query. Nothing here assumes a symbol's alphabet from its shape.
    /// </summary>
    private static List<WordPhone> BuildWordPhones(Word word)
    {
        if (word.Phones.Count > 0)
        {
            return word.Phones
                .OrderBy(p => p.Sequence)
                .Select(p => new WordPhone(
                    PhoneAlphabetConverter.ToCanonical(p.Symbol, p.Alphabet).Symbol,
                    p.StartSeconds,
                    p.EndSeconds,
                    IsPhoneLevelAligned: true))
                .Where(p => p.Symbol.Length > 0)
                .ToList();
        }

        if (string.IsNullOrEmpty(word.PhonemeSequence))
        {
            return [];
        }

        // No phone-level timing available, so every phoneme inherits the word's span - the same
        // approximation this builder has always made.
        return PhoneAlphabetConverter
            .ToCanonical(word.PhonemeSequence.Split(' ', StringSplitOptions.RemoveEmptyEntries), word.PhonemeAlphabet)
            .Select(p => new WordPhone(p.Symbol, null, null, IsPhoneLevelAligned: false))
            .ToList();
    }

    private readonly record struct WordPhone(string Symbol, double? StartSeconds, double? EndSeconds, bool IsPhoneLevelAligned);

    /// <summary>
    /// Same word-boundary-preserving flattening as <see cref="Build"/>, applied to a phonemized
    /// query instead of a stored transcript - so the query side of the matcher sees the same
    /// token shape (phonemes + boundaries) as the candidate side.
    /// </summary>
    public static List<PhoneToken> BuildQueryTokens(PhonemizationResult phonemization)
    {
        var tokens = new List<PhoneToken>();

        for (var i = 0; i < phonemization.Words.Count; i++)
        {
            if (i > 0)
            {
                tokens.Add(PhoneToken.Boundary);
            }

            tokens.AddRange(phonemization.Words[i].Phonemes.Select(PhoneToken.Phoneme));
        }

        return tokens;
    }
}
