using MemeSearcher.Core.Interfaces;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Tests.Benchmarks;

/// <summary>
/// Builds a corpus of synthetic media large enough to measure search cost against (#8).
///
/// The real ingestion path is used deliberately - real espeak, real SQLite, real phoneme stream
/// construction - because the point is to measure the actual system. A generator that wrote
/// plausible-looking rows straight into the database would measure a fiction, and the resulting
/// baselines would be worthless as a "before" for #9.
///
/// Deterministic: the same seed produces the same corpus, so two runs are comparable and a
/// regression is attributable to the code rather than to different input.
/// </summary>
public static class SyntheticCorpusGenerator
{
    /// <summary>
    /// Common English words, chosen for phonetic variety rather than frequency - the phoneme
    /// stream is what is being measured, so a vocabulary of near-homophones would produce an
    /// unrealistically compressible index in #9.
    /// </summary>
    private static readonly string[] Vocabulary =
    [
        "about", "after", "again", "always", "another", "because", "before", "between", "children",
        "country", "different", "enough", "every", "family", "father", "follow", "friend", "great",
        "group", "happen", "important", "large", "learn", "leave", "letter", "little", "money",
        "mother", "mountain", "narrow", "nature", "night", "number", "often", "order", "other",
        "paper", "people", "picture", "place", "plant", "point", "problem", "question", "really",
        "reason", "remember", "school", "second", "sentence", "several", "should", "simple",
        "small", "sometimes", "sound", "start", "still", "story", "student", "study", "system",
        "table", "thing", "think", "those", "though", "thought", "three", "through", "together",
        "toward", "under", "until", "voice", "water", "where", "whether", "which", "while",
        "white", "whole", "window", "without", "world", "would", "write", "young",
    ];

    public record GeneratedCorpus(int MediaCount, int SegmentCount, int WordCount, TimeSpan Elapsed);

    /// <summary>
    /// Generates and imports <paramref name="mediaCount"/> transcripts. Returns what was built so a
    /// benchmark can report corpus size alongside its timings - a number without its corpus is not
    /// a baseline.
    /// </summary>
    public static async Task<GeneratedCorpus> GenerateAsync(
        IDbContextFactory<MemeSearcherDbContext> factory,
        IPhonemizer phonemizer,
        string workingDirectory,
        int mediaCount,
        int segmentsPerMedia = 20,
        int seed = 20260823)
    {
        var random = new Random(seed);
        var started = DateTimeOffset.UtcNow;
        var wordCount = 0;

        for (var media = 0; media < mediaCount; media++)
        {
            var (path, words) = WriteSrt(random, workingDirectory, media, segmentsPerMedia);
            wordCount += words;

            await using var context = await factory.CreateDbContextAsync();
            var ingestion = new MediaIngestionService(
                context,
                TranscriptParserFactory.CreateDefault(),
                phonemizer,
                new UnusedTranscriptionProvider(),
                new MediaMetadataProbe(new FFprobeToolLocator()));

            await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        }

        return new GeneratedCorpus(
            mediaCount, mediaCount * segmentsPerMedia, wordCount, DateTimeOffset.UtcNow - started);
    }

    private static (string Path, int WordCount) WriteSrt(
        Random random, string directory, int index, int segmentsPerMedia)
    {
        var lines = new List<string>();
        var wordCount = 0;
        var cursor = TimeSpan.Zero;

        for (var cue = 1; cue <= segmentsPerMedia; cue++)
        {
            // 6-14 words per cue, roughly a spoken sentence.
            var length = random.Next(6, 15);
            var text = string.Join(' ', Enumerable.Range(0, length)
                .Select(_ => Vocabulary[random.Next(Vocabulary.Length)]));
            wordCount += length;

            var duration = TimeSpan.FromMilliseconds(length * 350);
            var end = cursor + duration;

            lines.Add(cue.ToString());
            lines.Add($"{Format(cursor)} --> {Format(end)}");
            lines.Add(text);
            lines.Add("");

            cursor = end + TimeSpan.FromMilliseconds(120);
        }

        // Each file must be unique in content, or MediaIngestionService's content-hash dedupe
        // (addendum §4) silently collapses the corpus to one item and the benchmark measures
        // nothing.
        lines.Insert(0, $"0\n00:00:00,000 --> 00:00:00,100\ncorpus item {index}\n");

        var path = Path.Combine(directory, $"synthetic-{index:D5}.srt");
        File.WriteAllLines(path, lines);
        return (path, wordCount + 2);
    }

    private static string Format(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2},{value.Milliseconds:D3}";
}
