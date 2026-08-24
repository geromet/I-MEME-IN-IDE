using MemeSearcher.Core.Interfaces;
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

/// <summary>#26: the transcript viewer's read path - real database, real import, proving cues come back in cue order with their real segment identity and timing rather than reconstructed from something else.</summary>
public class TranscriptViewServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-transcriptview-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-transcriptview-test-").FullName;

    private async Task<(IPhonemizer Phonemizer, IDbContextFactory<MemeSearcherDbContext> Factory)?> TrySetUpAsync()
    {
        var locator = new EspeakToolLocator();
        if (!(await locator.LocateAsync()).IsInstalled)
        {
            return null;
        }

        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        return (new EspeakPhonemizer(locator), factory);
    }

    [Fact]
    public async Task GetCuesAsync_ReturnsCuesInSequenceOrderWithTheirRealTiming()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (phonemizer, factory) = setup.Value;

        var srtPath = Path.Combine(_tempDir, "clip.srt");
        await File.WriteAllTextAsync(srtPath, """
            1
            00:00:01,000 --> 00:00:02,000
            hello there

            2
            00:00:05,000 --> 00:00:07,000
            general kenobi

            """);

        var ingestion = new MediaIngestionService(
            await factory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer,
            new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(null, srtPath, "en-US"));

        var service = new TranscriptViewService(factory);
        var cues = await service.GetCuesAsync(result.Media.Id);

        Assert.NotNull(cues);
        Assert.Equal(2, cues.Count);
        Assert.Equal("hello there", cues[0].Text);
        Assert.Equal(1.0, cues[0].StartSeconds);
        Assert.Equal(2.0, cues[0].EndSeconds);
        Assert.Equal("general kenobi", cues[1].Text);
        Assert.Equal(5.0, cues[1].StartSeconds);
    }

    [Fact]
    public async Task GetCuesAsync_UnknownMedia_ReturnsNull()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (_, factory) = setup.Value;
        var service = new TranscriptViewService(factory);

        var cues = await service.GetCuesAsync(Guid.NewGuid());

        Assert.Null(cues);
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
