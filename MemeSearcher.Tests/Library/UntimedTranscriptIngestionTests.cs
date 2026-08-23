using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Tests.Library;

/// <summary>
/// A plain-text import has no timeline, and that must survive into storage as null rather than
/// being interpolated across a fabricated 0-to-0 span (#32).
/// </summary>
public class UntimedTranscriptIngestionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-untimed-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-untimed-").FullName;

    private MemeSearcherDbContext CreateContext()
    {
        var context = new MemeSearcherDbContext(
            new DbContextOptionsBuilder<MemeSearcherDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public async Task ImportingPlainText_StoresNullTimingRatherThanZero()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return;
        }

        var path = Path.Combine(_tempDir, "notes.txt");
        await File.WriteAllTextAsync(path, "hello world\nsecond line\n");

        await using (var context = CreateContext())
        {
            var service = new MediaIngestionService(
                context, TranscriptParserFactory.CreateDefault(), new EspeakPhonemizer(locator),
                new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));

            await service.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        }

        await using var verify = CreateContext();
        var segments = await verify.Segments.Include(s => s.Words).ToListAsync();

        Assert.NotEmpty(segments);
        Assert.All(segments, s =>
        {
            Assert.Null(s.StartSeconds);
            Assert.Null(s.EndSeconds);
            Assert.NotEmpty(s.Words);
            Assert.All(s.Words, w =>
            {
                Assert.Null(w.StartSeconds);
                Assert.Null(w.EndSeconds);
            });
        });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempDir, recursive: true);
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        GC.SuppressFinalize(this);
    }
}
