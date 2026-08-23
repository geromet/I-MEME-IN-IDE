using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Library;

public class LibraryServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-libsvc-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-libsvc-test-").FullName;

    private async Task<(LibraryService Library, IDbContextFactory<MemeSearcherDbContext> Factory)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        var status = await locator.LocateAsync();
        if (!status.IsInstalled)
        {
            return null;
        }

        var dbContextFactory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await dbContextFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        return (new LibraryService(dbContextFactory), dbContextFactory);
    }

    private static async Task<Guid> ImportAsync(
        IDbContextFactory<MemeSearcherDbContext> factory, string tempDir, string fileName, string srtBody, string? mediaPath = null)
    {
        var path = Path.Combine(tempDir, fileName);
        await File.WriteAllTextAsync(path, srtBody);

        var phonemizer = new EspeakPhonemizer(new EspeakToolLocator());
        var ingestion = new MediaIngestionService(await factory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(mediaPath, path, "en-US"));
        return result.Media.Id;
    }

    [Fact]
    public async Task GetAllAsync_ReportsAccurateSegmentWordAndPhonemeCounts()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (library, factory) = setup.Value;

        await ImportAsync(factory, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            2
            00:00:03,000 --> 00:00:04,000
            a long bus

            """);

        var summaries = await library.GetAllAsync();

        var summary = Assert.Single(summaries);
        Assert.Equal(2, summary.SegmentCount);
        Assert.Equal(5, summary.WordCount); // "among us" (2) + "a long bus" (3)
        Assert.Equal(5, summary.PhonemizedWordCount);
    }

    [Fact]
    public async Task GetAllAsync_ReportsWhetherPlayableMediaWasAttached()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (library, factory) = setup.Value;

        await ImportAsync(factory, _tempDir, "transcript-only.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");
        await ImportAsync(factory, _tempDir, "with-media.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """, mediaPath);

        var summaries = await library.GetAllAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Single(summaries, s => !s.HasPlayableMedia);
        Assert.Single(summaries, s => s.HasPlayableMedia);
    }

    [Fact]
    public async Task GetPathsAsync_OnlyReturnsEntriesWithAPlayableMediaFile()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (library, factory) = setup.Value;

        var transcriptOnlyId = await ImportAsync(factory, _tempDir, "transcript-only.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        var mediaPath = Path.Combine(_tempDir, "clip.mp4");
        await File.WriteAllTextAsync(mediaPath, "not a real video, just needs to exist");
        var withMediaId = await ImportAsync(factory, _tempDir, "with-media.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """, mediaPath);

        var paths = await library.GetPathsAsync([transcriptOnlyId, withMediaId]);

        Assert.False(paths.ContainsKey(transcriptOnlyId)); // no playable file - must be absent, not mapped to the transcript
        Assert.Equal(mediaPath, paths[withMediaId]);
    }

    [Fact]
    public async Task RemoveAsync_CascadeDeletesTranscriptSegmentsAndWords()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (library, factory) = setup.Value;

        var mediaId = await ImportAsync(factory, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        await library.RemoveAsync(mediaId, deleteSourceFile: false);

        await using var context = await factory.CreateDbContextAsync();
        Assert.False(await context.Media.AnyAsync(m => m.Id == mediaId));
        Assert.False(await context.Transcripts.AnyAsync(t => t.MediaId == mediaId));
        Assert.Equal(0, await context.Segments.CountAsync());
        Assert.Equal(0, await context.Words.CountAsync());
    }

    [Fact]
    public async Task RemoveAsync_WithDeleteSourceFile_DeletesTheFileFromDisk()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (library, factory) = setup.Value;
        var path = Path.Combine(_tempDir, "clip.srt");
        var mediaId = await ImportAsync(factory, _tempDir, "clip.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);

        Assert.True(File.Exists(path));

        await library.RemoveAsync(mediaId, deleteSourceFile: true);

        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        Directory.Delete(_tempDir, recursive: true);
    }
}
