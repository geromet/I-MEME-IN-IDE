using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Transcripts;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Transcription;
using Microsoft.EntityFrameworkCore;
using CoreModels = MemeSearcher.Core.Models;

namespace MemeSearcher.Infrastructure.Library;

/// <summary>
/// Orchestrates the "Source file(s) -> identify media -> import transcript -> phonemize -> build
/// phoneme stream" pipeline (addendum §6). Indexing (candidate generation for search) is a
/// separate, independently rerunnable stage - see addendum §5, §28 - and is not done here.
/// </summary>
public class MediaIngestionService(
    MemeSearcherDbContext dbContext,
    TranscriptParserFactory parserFactory,
    IPhonemizer phonemizer)
{
    public async Task<MediaIngestionResult> ImportAsync(
        MediaIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var identityPath = request.MediaPath ?? request.TranscriptPath;
        var contentHash = await ContentHasher.ComputeSha256Async(identityPath, cancellationToken);

        var existing = await dbContext.Media
            .FirstOrDefaultAsync(m => m.ContentHash == contentHash, cancellationToken);

        // Re-import behavior (addendum §4): a file we've already indexed is not reprocessed.
        if (existing is not null)
        {
            return new MediaIngestionResult(MediaIngestionOutcome.AlreadyIndexed, existing);
        }

        var fileInfo = new FileInfo(identityPath);
        var now = DateTimeOffset.UtcNow;

        var media = new CoreModels.Media
        {
            Id = Guid.NewGuid(),
            Path = request.MediaPath ?? request.TranscriptPath,
            MediaFilePath = request.MediaPath,
            Title = request.Title,
            Language = request.Language,
            CreatedAt = now,
            UpdatedAt = now,
            ProcessingVersion = 1,
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            ContentHash = contentHash,
        };

        var parser = parserFactory.GetParser(request.TranscriptPath);
        var content = await File.ReadAllTextAsync(request.TranscriptPath, cancellationToken);
        var parsed = parser.Parse(content);

        var transcript = await BuildTranscriptAsync(media.Id, request.Language, parsed, cancellationToken);

        dbContext.Media.Add(media);
        dbContext.Transcripts.Add(transcript);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MediaIngestionResult(MediaIngestionOutcome.Imported, media);
    }

    private async Task<CoreModels.Transcript> BuildTranscriptAsync(
        Guid mediaId,
        string language,
        ParsedTranscript parsed,
        CancellationToken cancellationToken)
    {
        var transcript = new CoreModels.Transcript
        {
            Id = Guid.NewGuid(),
            MediaId = mediaId,
            Source = parsed.SourceFormat,
            Language = language,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var sequence = 0;
        foreach (var cue in parsed.Cues)
        {
            var normalizedText = TextNormalizer.Normalize(cue.Text);

            // One espeak-ng invocation per cue for now - simple and correct. If import throughput
            // on large corpora turns out to matter, batch multiple cues into a single stdin call
            // (see EspeakPhonemizer's one-line-per-input-line stdin behavior) rather than
            // reaching for a persistent worker prematurely (addendum §48: profile before optimizing).
            var phonemized = await phonemizer.PhonemizeAsync(normalizedText, language, cancellationToken);

            var segment = new CoreModels.Segment
            {
                Id = Guid.NewGuid(),
                TranscriptId = transcript.Id,
                Sequence = sequence++,
                StartSeconds = cue.StartSeconds,
                EndSeconds = cue.EndSeconds,
                Text = cue.Text,
                NormalizedText = normalizedText,
                Ipa = phonemized.Ipa,
                PhonemeSequence = JoinWordBoundaries(phonemized.Words),
            };

            segment.Words = BuildWords(segment.Id, phonemized.Words, cue.StartSeconds, cue.EndSeconds);

            transcript.Segments.Add(segment);
        }

        return transcript;
    }

    /// <summary>
    /// Preserves word boundaries with " | " per handoff §12/§17 - the fuzzy matcher decides later
    /// how cheaply to cross them, but the boundary itself must survive storage.
    /// </summary>
    private static string JoinWordBoundaries(IReadOnlyList<PhonemizedWord> words) =>
        string.Join(" | ", words.Select(w => string.Join(' ', w.Phonemes)));

    /// <summary>
    /// Word-level timing here is a proportional-by-character-count placeholder, not real alignment -
    /// see handoff §49/§50. It exists so a continuous phoneme stream can be built before an
    /// alignment provider ever runs; it must be overwritten once real alignment is available.
    /// </summary>
    private static List<CoreModels.Word> BuildWords(
        Guid segmentId,
        IReadOnlyList<PhonemizedWord> phonemizedWords,
        double startSeconds,
        double endSeconds)
    {
        if (phonemizedWords.Count == 0)
        {
            return [];
        }

        var totalChars = phonemizedWords.Sum(w => w.Text.Length);
        var duration = endSeconds - startSeconds;
        var cursor = startSeconds;

        var words = new List<CoreModels.Word>(phonemizedWords.Count);
        for (var i = 0; i < phonemizedWords.Count; i++)
        {
            var phonemizedWord = phonemizedWords[i];
            var share = totalChars > 0 ? (double)phonemizedWord.Text.Length / totalChars * duration : 0;
            var wordStart = cursor;
            var wordEnd = i == phonemizedWords.Count - 1 ? endSeconds : cursor + share;
            cursor = wordEnd;

            words.Add(new CoreModels.Word
            {
                Id = Guid.NewGuid(),
                SegmentId = segmentId,
                Sequence = i,
                Text = phonemizedWord.Text,
                NormalizedText = phonemizedWord.Text,
                Ipa = phonemizedWord.Ipa,
                PhonemeSequence = string.Join(' ', phonemizedWord.Phonemes),
                StartSeconds = wordStart,
                EndSeconds = wordEnd,
            });
        }

        return words;
    }
}
