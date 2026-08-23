namespace MemeSearcher.Core.Transcripts;

/// <summary>One transcript word matched to one aligned word, by position in their respective sequences.</summary>
public record WordCorrespondence(int TranscriptIndex, int AlignedIndex);

/// <summary>
/// Matches a transcript's word sequence to an aligner's word sequence by identity, using global
/// sequence alignment (Needleman-Wunsch) over normalized text.
///
/// This replaces bucketing aligned words into transcript segments by timestamp and accepting the
/// result whenever the counts happened to be equal (#30). That is not a safe join: a forced
/// aligner produces its own word boundaries from the audio, which legitimately differ from the
/// cue boundaries the transcriber produced, so a word sitting a few centiseconds outside its
/// segment's range drops out and the next segment's first word takes its place. The counts still
/// match, the guard still passes, and every word receives its neighbour's timing.
///
/// Cardinality is not identity. Matching on the words themselves cannot shift, because a shift
/// makes the text stop matching - which is precisely the signal the previous approach lacked.
///
/// Insertions and deletions are expected rather than exceptional: an aligner drops words it
/// cannot fit and may split or merge others, so the two sequences genuinely differ. Those
/// positions are left unmatched instead of being forced into a pairing.
/// </summary>
public static class WordSequenceAligner
{
    private enum Move : byte { Match, DeleteTranscript, DeleteAligned }

    private const double MatchScore = 1.0;
    private const double MismatchScore = -1.0;
    private const double GapScore = -0.5;

    /// <summary>
    /// Returns only the positions where both sides agree on the word. Mismatched substitutions are
    /// deliberately excluded: a pairing whose texts differ is exactly the case that must not be
    /// trusted, even though the alignment path may route through it.
    /// </summary>
    public static IReadOnlyList<WordCorrespondence> Align(
        IReadOnlyList<string> transcriptWords, IReadOnlyList<string> alignedWords)
    {
        var n = transcriptWords.Count;
        var m = alignedWords.Count;

        if (n == 0 || m == 0)
        {
            return [];
        }

        var normalizedTranscript = transcriptWords.Select(Normalize).ToArray();
        var normalizedAligned = alignedWords.Select(Normalize).ToArray();

        var score = new double[n + 1, m + 1];
        var move = new Move[n + 1, m + 1];

        for (var i = 1; i <= n; i++)
        {
            score[i, 0] = i * GapScore;
            move[i, 0] = Move.DeleteTranscript;
        }

        for (var j = 1; j <= m; j++)
        {
            score[0, j] = j * GapScore;
            move[0, j] = Move.DeleteAligned;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var isMatch = normalizedTranscript[i - 1] == normalizedAligned[j - 1];
                var diagonal = score[i - 1, j - 1] + (isMatch ? MatchScore : MismatchScore);
                var up = score[i - 1, j] + GapScore;
                var left = score[i, j - 1] + GapScore;

                if (diagonal >= up && diagonal >= left)
                {
                    score[i, j] = diagonal;
                    move[i, j] = Move.Match;
                }
                else if (up >= left)
                {
                    score[i, j] = up;
                    move[i, j] = Move.DeleteTranscript;
                }
                else
                {
                    score[i, j] = left;
                    move[i, j] = Move.DeleteAligned;
                }
            }
        }

        var correspondences = new List<WordCorrespondence>();
        var x = n;
        var y = m;

        while (x > 0 && y > 0)
        {
            switch (move[x, y])
            {
                case Move.Match:
                    if (normalizedTranscript[x - 1] == normalizedAligned[y - 1])
                    {
                        correspondences.Add(new WordCorrespondence(x - 1, y - 1));
                    }

                    x--;
                    y--;
                    break;

                case Move.DeleteTranscript:
                    x--;
                    break;

                default:
                    y--;
                    break;
            }
        }

        correspondences.Reverse();
        return correspondences;
    }

    /// <summary>
    /// Compares words the way the rest of the pipeline does - the aligner is fed the transcript's
    /// own text, so differences are punctuation and case, not spelling.
    /// </summary>
    private static string Normalize(string word) => TextNormalizer.Normalize(word);
}
