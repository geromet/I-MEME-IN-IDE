using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Tests.Library;

public sealed class LibraryPipelineStateTests
{
    private sealed class DbFactory(DbContextOptions<MemeSearcherDbContext> options)
        : IDbContextFactory<MemeSearcherDbContext>
    {
        public MemeSearcherDbContext CreateDbContext() => new(options);

        public Task<MemeSearcherDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task GetAllAsync_ProjectsTranscriptPhonemeAlignmentAndIndexStatePerMedia()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imeme-pipeline-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<MemeSearcherDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var factory = new DbFactory(options);

        try
        {
            var populatedId = Guid.NewGuid();
            var emptyId = Guid.NewGuid();
            var transcriptId = Guid.NewGuid();
            var segmentId = Guid.NewGuid();
            var firstWordId = Guid.NewGuid();
            var secondWordId = Guid.NewGuid();
            var thirdWordId = Guid.NewGuid();

            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();

                db.Media.AddRange(
                    new Media
                    {
                        Id = populatedId,
                        Path = "/fixture/populated.srt",
                        Language = "en-US",
                        ContentHash = "populated",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    },
                    new Media
                    {
                        Id = emptyId,
                        Path = "/fixture/empty.mp4",
                        MediaFilePath = "/fixture/empty.mp4",
                        Language = "en-US",
                        ContentHash = "empty",
                        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    });

                db.Transcripts.Add(new Transcript
                {
                    Id = transcriptId,
                    MediaId = populatedId,
                    Source = "fixture",
                    Language = "en-US",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.Segments.Add(new Segment
                {
                    Id = segmentId,
                    TranscriptId = transcriptId,
                    Sequence = 0,
                    Text = "one two three",
                });
                db.Words.AddRange(
                    new Word
                    {
                        Id = firstWordId,
                        SegmentId = segmentId,
                        Sequence = 0,
                        Text = "one",
                        PhonemeSequence = "wʌn",
                    },
                    new Word
                    {
                        Id = secondWordId,
                        SegmentId = segmentId,
                        Sequence = 1,
                        Text = "two",
                        PhonemeSequence = "tuː",
                    },
                    new Word
                    {
                        Id = thirdWordId,
                        SegmentId = segmentId,
                        Sequence = 2,
                        Text = "three",
                    });
                db.Phones.AddRange(
                    new Phone { Id = Guid.NewGuid(), WordId = firstWordId, Sequence = 0, Symbol = "W" },
                    new Phone { Id = Guid.NewGuid(), WordId = firstWordId, Sequence = 1, Symbol = "AH" },
                    new Phone { Id = Guid.NewGuid(), WordId = secondWordId, Sequence = 0, Symbol = "T" });
                db.PhoneNGramPostings.Add(new PhoneNGramPosting
                {
                    Id = Guid.NewGuid(),
                    MediaId = populatedId,
                    NGram = "W AH T",
                    StreamPosition = 0,
                });

                await db.SaveChangesAsync();
            }

            var summaries = await new LibraryService(factory).GetAllAsync();

            var populated = Assert.Single(summaries, x => x.Id == populatedId);
            Assert.True(populated.HasTranscript);
            Assert.Equal(1, populated.SegmentCount);
            Assert.Equal(3, populated.WordCount);
            Assert.Equal(2, populated.PhonemizedWordCount);
            Assert.Equal(2, populated.AlignedWordCount); // two words, not three Phone rows
            Assert.True(populated.HasIndexPostings);

            var row = new MediaRowViewModel(populated);
            Assert.Equal("Transcript: ready", row.TranscriptStateDisplay);
            Assert.Equal("Phonemes: partial (2/3)", row.PhonemeStateDisplay);
            Assert.Equal("Alignment: partial (2/3)", row.AlignmentStateDisplay);
            Assert.Equal("Index: ready", row.IndexStateDisplay);

            var empty = Assert.Single(summaries, x => x.Id == emptyId);
            Assert.False(empty.HasTranscript);
            Assert.Equal(0, empty.WordCount);
            Assert.Equal(0, empty.AlignedWordCount);
            Assert.False(empty.HasIndexPostings);

            var emptyRow = new MediaRowViewModel(empty);
            Assert.Equal("Transcript: none", emptyRow.TranscriptStateDisplay);
            Assert.Equal("Phonemes: none", emptyRow.PhonemeStateDisplay);
            Assert.Equal("Alignment: none", emptyRow.AlignmentStateDisplay);
            Assert.Equal("Index: none", emptyRow.IndexStateDisplay);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void MediaRow_ReportsFullCoverageOnlyWhenEveryWordHasStageData()
    {
        var summary = new MediaSummary(
            Guid.NewGuid(),
            "full",
            "/fixture/full.srt",
            false,
            TimeSpan.Zero,
            "en-US",
            DateTimeOffset.UtcNow,
            true,
            1,
            4,
            4,
            4,
            true,
            true);

        var row = new MediaRowViewModel(summary);

        Assert.Equal("Phonemes: full (4/4)", row.PhonemeStateDisplay);
        Assert.Equal("Alignment: full (4/4)", row.AlignmentStateDisplay);
    }
}
