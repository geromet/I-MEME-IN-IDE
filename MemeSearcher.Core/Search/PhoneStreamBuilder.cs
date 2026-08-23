using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Models;

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
        var isFirstWord = true;

        foreach (var transcript in transcripts.OrderBy(t => t.CreatedAt))
        {
            foreach (var segment in transcript.Segments.OrderBy(s => s.Sequence))
            {
                foreach (var word in segment.Words.OrderBy(w => w.Sequence))
                {
                    if (string.IsNullOrEmpty(word.PhonemeSequence))
                    {
                        continue;
                    }

                    if (!isFirstWord)
                    {
                        stream.Add(PhoneStreamEntry.Boundary());
                    }

                    foreach (var symbol in word.PhonemeSequence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        stream.Add(PhoneStreamEntry.Phoneme(symbol, segment.Id, word.Id, word.Text, word.StartSeconds, word.EndSeconds));
                    }

                    isFirstWord = false;
                }
            }
        }

        return stream;
    }

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
