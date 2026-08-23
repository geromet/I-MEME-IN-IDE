using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Phonetics;
using MemeSearcher.Core.Transcripts;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Transcription;
using Microsoft.EntityFrameworkCore;
using CoreModels = MemeSearcher.Core.Models;

namespace MemeSearcher.Infrastructure.Library;

/// <summary>
/// Orchestrates the "Source file(s) -> identify media -> transcribe (if needed) -> phonemize ->
/// build phoneme stream" pipeline (addendum §6). As of Milestone 3, "import transcript" can mean
/// either parsing an existing SRT/VTT/text file or transcribing the media directly via
/// ITranscriptionProvider when no transcript file was given. Indexing (candidate generation for
/// search) is a separate, independently rerunnable stage - see addendum §5, §28 - and is not done
/// here.
/// </summary>
public class MediaIngestionService(
    MemeSearcherDbContext dbContext,
    TranscriptParserFactory parserFactory,
    IPhonemizer phonemizer,
    ITranscriptionProvider transcriptionProvider,
    MediaMetadataProbe metadataProbe,
    IAlignmentProvider? alignmentProvider = null)
{
    public async Task<MediaIngestionResult> ImportAsync(
        MediaIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MediaPath is null && request.TranscriptPath is null)
        {
            throw new ArgumentException("At least one of MediaPath or TranscriptPath must be provided.", nameof(request));
        }

        var identityPath = request.MediaPath ?? request.TranscriptPath!;
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

        // Duration is only knowable when there's an actual media file - FFmpeg/ffprobe is
        // optional (addendum Milestone 3), so a missing ffprobe just leaves this at zero rather
        // than failing the import.
        var duration = request.MediaPath is not null
            ? await metadataProbe.TryGetDurationAsync(request.MediaPath, cancellationToken)
            : null;

        var media = new CoreModels.Media
        {
            Id = Guid.NewGuid(),
            Path = identityPath,
            MediaFilePath = request.MediaPath,
            Title = request.Title,
            Language = request.Language,
            Duration = duration ?? TimeSpan.Zero,
            CreatedAt = now,
            UpdatedAt = now,
            ProcessingVersion = 1,
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            ContentHash = contentHash,
        };

        var (parsed, provenance) = await ResolveTranscriptAsync(request, cancellationToken);

        // Provenance is only known when this import actually ran a transcription - an imported SRT
        // was produced by something outside this app entirely (#24).
        media.TranscriptionModel = provenance?.Model;
        media.TranscriptionDevice = provenance?.Device;
        media.TranscriptionComputeType = provenance?.ComputeType;

        var transcript = await BuildTranscriptAsync(media.Id, request.Language, parsed, cancellationToken);

        dbContext.Media.Add(media);
        dbContext.Transcripts.Add(transcript);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MediaIngestionResult(MediaIngestionOutcome.Imported, media);
    }

    /// <summary>
    /// Milestone 6 / addendum §30: realigns an already-imported media item's word (and, with a
    /// phone-level-capable provider like MFA, phone) timing - without retranscribing or
    /// rephonemizing. This is the "reprocess phonetics without retranscribing" pipeline
    /// independence the addendum asks for, applied specifically to alignment.
    /// </summary>
    public async Task<RealignmentResult> RealignAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        if (alignmentProvider is null)
        {
            throw new InvalidOperationException("No alignment provider is configured.");
        }

        var media = await dbContext.Media.FindAsync([mediaId], cancellationToken);
        if (media is null)
        {
            throw new ArgumentException($"No media found with id '{mediaId}'.", nameof(mediaId));
        }

        if (media.MediaFilePath is null)
        {
            throw new InvalidOperationException("Media has no playable file to align against - transcript-only imports cannot be realigned.");
        }

        var transcripts = await dbContext.Transcripts
            .Where(t => t.MediaId == mediaId)
            .Include(t => t.Segments)
            .ThenInclude(s => s.Words)
            .ThenInclude(w => w.Phones)
            .ToListAsync(cancellationToken);

        if (transcripts.Count == 0)
        {
            throw new InvalidOperationException("Media has no transcript to realign.");
        }

        var orderedSegments = transcripts.SelectMany(t => t.Segments).OrderBy(s => s.Sequence).ToList();
        var fullText = string.Join(" ", orderedSegments.Select(s => s.Text));

        var alignment = await alignmentProvider.AlignAsync(media.MediaFilePath, fullText, cancellationToken);

        var phoneAlphabet = ValidateDeclaredAlphabet(alignment);

        var updatedWordCount = 0;
        var updatedPhoneCount = 0;

        foreach (var segment in orderedSegments)
        {
            var segmentWords = segment.Words.OrderBy(w => w.Sequence).ToList();

            // Bucket aligned words by which segment's time range they fall in. Only apply when
            // the count matches this segment's own word split - same "don't guess on mismatch"
            // discipline as BuildWords' interpolation fallback (handoff §49/§50).
            var wordsInRange = alignment.Words
                .Where(w => w.StartSeconds >= segment.StartSeconds && w.StartSeconds < segment.EndSeconds)
                .OrderBy(w => w.StartSeconds)
                .ToList();

            if (wordsInRange.Count != segmentWords.Count)
            {
                continue;
            }

            for (var i = 0; i < segmentWords.Count; i++)
            {
                segmentWords[i].StartSeconds = wordsInRange[i].StartSeconds;
                segmentWords[i].EndSeconds = wordsInRange[i].EndSeconds;
                updatedWordCount++;

                if (alignment.Phones is null)
                {
                    continue;
                }

                var phonesInWord = alignment.Phones
                    .Where(p => p.StartSeconds >= segmentWords[i].StartSeconds && p.StartSeconds < segmentWords[i].EndSeconds)
                    .OrderBy(p => p.StartSeconds)
                    .ToList();

                if (phonesInWord.Count == 0)
                {
                    continue;
                }

                // Replace any existing (sparse/placeholder) phone rows for this word.
                dbContext.Phones.RemoveRange(segmentWords[i].Phones);
                segmentWords[i].Phones.Clear();

                var newPhones = phonesInWord.Select((p, seq) => new CoreModels.Phone
                {
                    Id = Guid.NewGuid(),
                    WordId = segmentWords[i].Id,
                    Sequence = seq,
                    Symbol = p.Symbol,
                    Alphabet = phoneAlphabet!.Value,
                    StartSeconds = p.StartSeconds,
                    EndSeconds = p.EndSeconds,
                }).ToList();

                // Added explicitly via the DbSet, NOT also through
                // segmentWords[i].Phones.Add(...) - a new child entity discovered purely through
                // a navigation collection on an already-tracked parent can get its EntityState
                // inferred as Modified instead of Added when its key was assigned client-side (a
                // real EF Core graph-tracking gotcha, not a hypothetical one: it reproduced as a
                // DbUpdateConcurrencyException on every run until fixed this way). Doing both
                // would double-insert each phone, since AddRange already tracks them as Added and
                // EF's relationship fix-up populates the navigation collection on its own.
                dbContext.Phones.AddRange(newPhones);

                updatedPhoneCount += newPhones.Count;
            }
        }

        media.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RealignmentResult(updatedWordCount, updatedPhoneCount);
    }

    /// <summary>
    /// Parses an existing transcript file if one was given; otherwise transcribes the media
    /// directly (Milestone 3). Both paths converge on the same ParsedTranscript shape so the rest
    /// of the pipeline (phonemization, word-building) doesn't need to know which happened.
    /// </summary>
    private async Task<(ParsedTranscript Parsed, TranscriptionProvenance? Provenance)> ResolveTranscriptAsync(
        MediaIngestionRequest request, CancellationToken cancellationToken)
    {
        if (request.TranscriptPath is not null)
        {
            var parser = parserFactory.GetParser(request.TranscriptPath);
            var content = await File.ReadAllTextAsync(request.TranscriptPath, cancellationToken);
            return (parser.Parse(content), null);
        }

        var transcribed = await transcriptionProvider.TranscribeAsync(request.MediaPath!, request.Language, cancellationToken);
        var cues = transcribed.Segments
            .Select(s => new ParsedCue(
                s.StartSeconds,
                s.EndSeconds,
                s.Text,
                s.Words?.Select(w => new ParsedWord(w.Text, w.StartSeconds, w.EndSeconds)).ToList()))
            .ToList();
        return (new ParsedTranscript(transcriptionProvider.ProviderName, cues), transcribed.Provenance);
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

            segment.Words = BuildWords(
                segment.Id, phonemized.Words, cue.StartSeconds, cue.EndSeconds, cue.Words, phonemizer.Alphabet);

            transcript.Segments.Add(segment);
        }

        ValidatePhonemizerAlphabet(transcript);

        return transcript;
    }

    /// <summary>
    /// Same declaration-versus-reality check as <see cref="ValidateDeclaredAlphabet"/>, applied to
    /// the phonemizer (#18). Run once over the whole transcript rather than per cue, because
    /// detection needs a reasonable sample: a single short word can be entirely ASCII and
    /// therefore undecidable, while a transcript's worth of symbols almost never is.
    /// </summary>
    private void ValidatePhonemizerAlphabet(CoreModels.Transcript transcript)
    {
        var symbols = transcript.Segments
            .SelectMany(s => s.Words)
            .Select(w => w.PhonemeSequence)
            .Where(sequence => !string.IsNullOrEmpty(sequence))
            .SelectMany(sequence => sequence!.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var detected = PhoneAlphabetDetector.Detect(symbols);

        if (detected.IsConfident && detected.Alphabet != phonemizer.Alphabet)
        {
            throw new InvalidOperationException(
                $"Phonemizer '{phonemizer.ProviderName}' declares {phonemizer.Alphabet} but its output "
                + $"looks like {detected.Alphabet}: {detected.Explanation} Refusing to store mis-tagged "
                + "phonemes.");
        }
    }

    /// <summary>
    /// Preserves word boundaries with " | " per handoff §12/§17 - the fuzzy matcher decides later
    /// how cheaply to cross them, but the boundary itself must survive storage.
    /// </summary>
    private static string JoinWordBoundaries(IReadOnlyList<PhonemizedWord> words) =>
        string.Join(" | ", words.Select(w => string.Join(' ', w.Phonemes)));

    /// <summary>
    /// Milestone 5: uses real per-word timing (from the transcription/alignment provider) when
    /// it's available and lines up 1:1 with the phonemizer's word split; otherwise falls back to
    /// the proportional-by-character-count placeholder from earlier milestones (handoff §49/§50:
    /// predicted vs actual pronunciation/timing). The counts can disagree - WhisperX's own
    /// tokenization doesn't always match TextNormalizer.Tokenize exactly (contractions, numbers) -
    /// and silently misaligning word N's real timing to phonemized word M would be worse than the
    /// honest placeholder, so any mismatch just falls back rather than guessing.
    /// </summary>
    /// <summary>
    /// Checks the alignment provider's declared alphabet against what it actually wrote, and fails
    /// loudly on a mismatch (#18).
    ///
    /// The declaration is the source of truth - MFA's english_us_arpa models emit ARPABET and that
    /// is not in doubt. Detection is the safety net: it catches a provider reconfigured to a
    /// different model, or a future provider whose declaration is simply wrong. Getting this wrong
    /// silently is the entire failure mode this issue exists to close - mis-tagged phones convert
    /// to the wrong canonical symbols and the corpus quietly stops matching, with no error
    /// anywhere.
    ///
    /// Only a *confident* contradiction throws. Short or ASCII-only phone sets are genuinely
    /// undecidable, and refusing an import over "I could not tell" would be worse than trusting
    /// the declaration.
    /// </summary>
    private PhoneAlphabet? ValidateDeclaredAlphabet(AlignmentResult alignment)
    {
        if (alignment.Phones is null || alignment.Phones.Count == 0)
        {
            return null;
        }

        var declared = alignmentProvider!.PhoneAlphabet
            ?? throw new InvalidOperationException(
                $"Alignment provider '{alignmentProvider.ProviderName}' returned {alignment.Phones.Count} "
                + "phones but declares no phone alphabet, so they cannot be stored or interpreted.");

        var detected = PhoneAlphabetDetector.Detect(alignment.Phones.Select(p => p.Symbol));

        if (detected.IsConfident && detected.Alphabet != declared)
        {
            throw new InvalidOperationException(
                $"Alignment provider '{alignmentProvider.ProviderName}' declares {declared} but its output "
                + $"looks like {detected.Alphabet}: {detected.Explanation} Refusing to store mis-tagged "
                + "phones - they would convert to the wrong canonical symbols and silently stop matching.");
        }

        return declared;
    }

    private static List<CoreModels.Word> BuildWords(
        Guid segmentId,
        IReadOnlyList<PhonemizedWord> phonemizedWords,
        double startSeconds,
        double endSeconds,
        IReadOnlyList<ParsedWord>? realWords,
        PhoneAlphabet alphabet)
    {
        if (phonemizedWords.Count == 0)
        {
            return [];
        }

        return realWords is not null && realWords.Count == phonemizedWords.Count
            ? BuildWordsFromRealTiming(segmentId, phonemizedWords, realWords, alphabet)
            : BuildWordsFromInterpolation(segmentId, phonemizedWords, startSeconds, endSeconds, alphabet);
    }

    private static List<CoreModels.Word> BuildWordsFromRealTiming(
        Guid segmentId, IReadOnlyList<PhonemizedWord> phonemizedWords, IReadOnlyList<ParsedWord> realWords,
        PhoneAlphabet alphabet)
    {
        var words = new List<CoreModels.Word>(phonemizedWords.Count);

        for (var i = 0; i < phonemizedWords.Count; i++)
        {
            var phonemizedWord = phonemizedWords[i];

            words.Add(new CoreModels.Word
            {
                Id = Guid.NewGuid(),
                SegmentId = segmentId,
                Sequence = i,
                Text = phonemizedWord.Text,
                NormalizedText = phonemizedWord.Text,
                Ipa = phonemizedWord.Ipa,
                PhonemeSequence = string.Join(' ', phonemizedWord.Phonemes),
                PhonemeAlphabet = alphabet,
                StartSeconds = realWords[i].StartSeconds,
                EndSeconds = realWords[i].EndSeconds,
            });
        }

        return words;
    }

    private static List<CoreModels.Word> BuildWordsFromInterpolation(
        Guid segmentId, IReadOnlyList<PhonemizedWord> phonemizedWords, double startSeconds, double endSeconds,
        PhoneAlphabet alphabet)
    {
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
                PhonemeAlphabet = alphabet,
                StartSeconds = wordStart,
                EndSeconds = wordEnd,
            });
        }

        return words;
    }
}
