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

    [Fact]
    public async Task GetAllAsync_NewlyImportedMedia_DefaultsToSelectedForSearch()
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

            """);

        var summary = Assert.Single(await library.GetAllAsync());
        Assert.True(summary.IsSelectedForSearch);
    }

    /// <summary>Milestone 13 exit criterion: "Selection survives restart" - a fresh LibraryService/DbContextFactory against the same file stands in for a restart.</summary>
    [Fact]
    public async Task SetSelectedAsync_PersistsAcrossANewServiceInstanceAgainstTheSameDatabase()
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

        await library.SetSelectedAsync(mediaId, false);

        // A brand new LibraryService over the same on-disk database - nothing in-memory survives
        // this, exactly like relaunching the app against its persisted database would.
        var reopened = new LibraryService(factory);
        var summary = Assert.Single(await reopened.GetAllAsync());
        Assert.False(summary.IsSelectedForSearch);
    }

    [Fact]
    public async Task GetSelectionSummaryAsync_ReportsOnlySelectedIdsAndTheTotal()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (library, factory) = setup.Value;

        var keptId = await ImportAsync(factory, _tempDir, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);
        var excludedId = await ImportAsync(factory, _tempDir, "b.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);

        await library.SetSelectedAsync(excludedId, false);

        var (selectedIds, total) = await library.GetSelectionSummaryAsync();

        Assert.Equal(2, total);
        Assert.Equal([keptId], selectedIds);
    }

    [Fact]
    public async Task SetAllSelectedAndInvertSelection_UpdateEveryMediaItem()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (library, factory) = setup.Value;

        var idA = await ImportAsync(factory, _tempDir, "a.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            among us

            """);
        var idB = await ImportAsync(factory, _tempDir, "b.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);

        await library.SetAllSelectedAsync(false);
        var (noneSelected, _) = await library.GetSelectionSummaryAsync();
        Assert.Empty(noneSelected);

        await library.InvertSelectionAsync();
        var (allSelected, _) = await library.GetSelectionSummaryAsync();
        Assert.Equal(new[] { idA, idB }.OrderBy(id => id), allSelected.OrderBy(id => id));
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
