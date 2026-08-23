using MemeSearcher.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MemeSearcher.Tests.Library;

/// <summary>
/// The MakeTimingNullable migration converts the old 0/0 sentinel to null - but only for
/// transcripts that genuinely have no timeline (#32).
///
/// The scoping is the whole point. Nulling every zero would be simpler and destructive: an SRT's
/// first cue legitimately starts at 0.0, and that is real timing, not a placeholder.
/// </summary>
public class TimingBackfillTests : IDisposable
{
    private const string MigrationBeforeNullableTiming = "20260823162423_AddPhoneAlphabetTags";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-timing-{Guid.NewGuid():N}.db");

    private MemeSearcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MemeSearcherDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void Migration_NullsTheSentinelForTextTranscriptsAndKeepsRealZerosForSrt()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(MigrationBeforeNullableTiming);
        }

        var textWordId = Guid.NewGuid();
        var srtWordId = Guid.NewGuid();

        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            InsertTranscript(connection, "text", textWordId);
            InsertTranscript(connection, "srt", srtWordId);
        }

        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using var migrated = CreateContext();

        var fromText = migrated.Words.Single(w => w.Id == textWordId);
        Assert.Null(fromText.StartSeconds);
        Assert.Null(fromText.EndSeconds);

        // A real 0.0 from a timed format must survive - it is a timestamp, not a placeholder.
        var fromSrt = migrated.Words.Single(w => w.Id == srtWordId);
        Assert.Equal(0, fromSrt.StartSeconds);
        Assert.Equal(0, fromSrt.EndSeconds);
    }

    private static void InsertTranscript(SqliteConnection connection, string source, Guid wordId)
    {
        var mediaId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");

        Execute(connection,
            "INSERT INTO Media (Id, Path, Duration, Language, CreatedAt, UpdatedAt, ProcessingVersion, "
            + "FileSize, LastModified, ContentHash) VALUES ($id, $path, '00:00:03', 'en-US', $now, $now, 1, 0, $now, $hash)",
            ("$id", mediaId), ("$now", now), ("$path", $"/tmp/{source}.txt"), ("$hash", source + "-hash"));

        Execute(connection,
            "INSERT INTO Transcripts (Id, MediaId, Source, Language, CreatedAt) "
            + "VALUES ($id, $mediaId, $source, 'en-US', $now)",
            ("$id", transcriptId), ("$mediaId", mediaId), ("$source", source), ("$now", now));

        Execute(connection,
            "INSERT INTO Segments (Id, TranscriptId, Sequence, StartSeconds, EndSeconds, Text) "
            + "VALUES ($id, $transcriptId, 0, 0, 0, 'hello')",
            ("$id", segmentId), ("$transcriptId", transcriptId));

        Execute(connection,
            "INSERT INTO Words (Id, SegmentId, Sequence, Text, StartSeconds, EndSeconds, PhonemeAlphabet) "
            + "VALUES ($id, $segmentId, 0, 'hello', 0, 0, 'Ipa')",
            ("$id", wordId), ("$segmentId", segmentId));
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(
                name, value is Guid guid ? guid.ToString().ToUpperInvariant() : value);
        }

        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        GC.SuppressFinalize(this);
    }
}
