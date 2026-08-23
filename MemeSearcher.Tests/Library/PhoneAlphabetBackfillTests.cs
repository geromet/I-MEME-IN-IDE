using MemeSearcher.Core.Phonetics;
using MemeSearcher.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MemeSearcher.Tests.Library;

/// <summary>
/// The AddPhoneAlphabetTags migration backfills existing Phone rows as ARPABET rather than letting
/// them take the column default (#18).
///
/// This is a data migration and it is not optional: Phone rows have exactly one writer -
/// RealignAsync, fed by IAlignmentProvider - and the only provider that has ever produced phones is
/// MFA, whose english_us_arpa models emit ARPABET. Leaving pre-existing rows tagged Ipa would mean
/// converting ARPABET symbols as though they were already canonical, silently corrupting every
/// aligned word in the corpus.
/// </summary>
public class PhoneAlphabetBackfillTests : IDisposable
{
    private const string MigrationBeforeAlphabetTags = "20260823155412_AddTranscriptionProvenance";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-backfill-{Guid.NewGuid():N}.db");

    private MemeSearcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MemeSearcherDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options);

    [Fact]
    public void Migration_TagsPreExistingPhoneRowsAsArpabetWithoutRewritingTheSymbol()
    {
        MigrateToStateBeforeTheTagsExisted();

        var phoneId = Guid.NewGuid();

        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            InsertPreMigrationPhone(connection, phoneId);
        }

        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using var migrated = CreateContext();
        var phone = migrated.Phones.Single(p => p.Id == phoneId);

        Assert.Equal(PhoneAlphabet.Arpabet, phone.Alphabet);

        // Storage keeps what the provider wrote; conversion happens on read. A migration that
        // rewrote symbols in place would make a conversion-table fix require re-running alignment
        // against source media that may no longer exist.
        Assert.Equal("AH0", phone.Symbol);
    }

    /// <summary>
    /// Writes a Phone row the way one existed before alphabets were tracked, along with the
    /// Media/Transcript/Segment/Word chain its foreign keys require.
    /// </summary>
    private static void InsertPreMigrationPhone(SqliteConnection connection, Guid phoneId)
    {
        var mediaId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();
        var wordId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");

        Execute(connection,
            "INSERT INTO Media (Id, Path, Duration, Language, CreatedAt, UpdatedAt, ProcessingVersion, "
            + "FileSize, LastModified, ContentHash) VALUES ($id, '/tmp/clip.mp4', '00:00:03', 'en-US', "
            + "$now, $now, 1, 0, $now, 'hash')",
            ("$id", mediaId), ("$now", now));

        Execute(connection,
            "INSERT INTO Transcripts (Id, MediaId, Source, Language, CreatedAt) "
            + "VALUES ($id, $mediaId, 'srt', 'en-US', $now)",
            ("$id", transcriptId), ("$mediaId", mediaId), ("$now", now));

        Execute(connection,
            "INSERT INTO Segments (Id, TranscriptId, Sequence, StartSeconds, EndSeconds, Text) "
            + "VALUES ($id, $transcriptId, 0, 1.0, 3.0, 'hello')",
            ("$id", segmentId), ("$transcriptId", transcriptId));

        Execute(connection,
            "INSERT INTO Words (Id, SegmentId, Sequence, Text, StartSeconds, EndSeconds) "
            + "VALUES ($id, $segmentId, 0, 'hello', 1.0, 1.7)",
            ("$id", wordId), ("$segmentId", segmentId));

        Execute(connection,
            "INSERT INTO Phones (Id, WordId, Sequence, Symbol, StartSeconds, EndSeconds) "
            + "VALUES ($id, $wordId, 0, 'AH0', 1.0, 1.2)",
            ("$id", phoneId), ("$wordId", wordId));
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            // EF Core's SQLite provider stores Guid as uppercase TEXT; matching that matters
            // because these rows are inserted raw and read back through EF.
            command.Parameters.AddWithValue(
                name, value is Guid guid ? guid.ToString().ToUpperInvariant() : value);
        }

        command.ExecuteNonQuery();
    }

    private void MigrateToStateBeforeTheTagsExisted()
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate(MigrationBeforeAlphabetTags);
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
