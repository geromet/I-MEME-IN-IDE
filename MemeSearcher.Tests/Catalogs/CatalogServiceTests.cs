using MemeSearcher.Infrastructure.Catalogs;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Phonetics;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.Transcription;
using MemeSearcher.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Catalogs;

/// <summary>Milestone 17 (#20): catalog CRUD, membership, and the cascade-delete exit criteria in both directions.</summary>
public class CatalogServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-catalogsvc-test-{Guid.NewGuid():N}.db");
    private readonly string _tempDir = Directory.CreateTempSubdirectory("memesearcher-catalogsvc-test-").FullName;

    private async Task<(CatalogService Catalogs, LibraryService Library, IDbContextFactory<MemeSearcherDbContext> Factory)?> TrySetUpAsync()
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

        return (new CatalogService(dbContextFactory), new LibraryService(dbContextFactory), dbContextFactory);
    }

    private static async Task<Guid> ImportAsync(
        IDbContextFactory<MemeSearcherDbContext> factory, string tempDir, string fileName, string srtBody)
    {
        var path = Path.Combine(tempDir, fileName);
        await File.WriteAllTextAsync(path, srtBody);

        var phonemizer = new EspeakPhonemizer(new EspeakToolLocator());
        var ingestion = new MediaIngestionService(await factory.CreateDbContextAsync(), TranscriptParserFactory.CreateDefault(), phonemizer, new UnusedTranscriptionProvider(), new MediaMetadataProbe(new FFprobeToolLocator()));
        var result = await ingestion.ImportAsync(new MediaIngestionRequest(null, path, "en-US"));
        return result.Media.Id;
    }

    private const string OneLineSrt = """
        1
        00:00:01,000 --> 00:00:02,000
        among us

        """;

    [Fact]
    public async Task CreateAsync_NewCatalog_HasNoMembersAndSurvivesAFreshServiceInstance()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (catalogs, _, factory) = setup.Value;

        var id = await catalogs.CreateAsync("Vine compilations", "Short-form clips");

        var reopened = new CatalogService(factory);
        var summary = Assert.Single(await reopened.GetAllAsync());
        Assert.Equal(id, summary.Id);
        Assert.Equal("Vine compilations", summary.Name);
        Assert.Equal("Short-form clips", summary.Description);
        Assert.Equal(0, summary.MemberCount);
    }

    [Fact]
    public async Task SetMemberAsync_AddsAndRemovesMembership()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (catalogs, _, factory) = setup.Value;

        var catalogId = await catalogs.CreateAsync("Catalog", null);
        var mediaId = await ImportAsync(factory, _tempDir, "clip.srt", OneLineSrt);

        await catalogs.SetMemberAsync(catalogId, mediaId, true);
        Assert.Equal([mediaId], await catalogs.GetMemberIdsAsync(catalogId));

        var summary = Assert.Single(await catalogs.GetAllAsync());
        Assert.Equal(1, summary.MemberCount);

        await catalogs.SetMemberAsync(catalogId, mediaId, false);
        Assert.Empty(await catalogs.GetMemberIdsAsync(catalogId));
    }

    [Fact]
    public async Task RemovingAMediaItemFromTheLibrary_RemovesItFromEveryCatalogWithoutOrphaningRows()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (catalogs, library, factory) = setup.Value;

        var mediaId = await ImportAsync(factory, _tempDir, "clip.srt", OneLineSrt);
        var catalogA = await catalogs.CreateAsync("A", null);
        var catalogB = await catalogs.CreateAsync("B", null);
        await catalogs.SetMemberAsync(catalogA, mediaId, true);
        await catalogs.SetMemberAsync(catalogB, mediaId, true);

        await library.RemoveAsync(mediaId, deleteSourceFile: false);

        await using var context = await factory.CreateDbContextAsync();
        Assert.False(await context.CatalogMedia.AnyAsync(cm => cm.MediaId == mediaId));

        // Both catalogs themselves must survive - only the join rows are gone.
        Assert.Equal(2, await context.Catalogs.CountAsync());
    }

    [Fact]
    public async Task DeletingACatalog_RemovesOnlyItsJoinRows_NeverTheMediaItPointedAt()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (catalogs, _, factory) = setup.Value;

        var mediaId = await ImportAsync(factory, _tempDir, "clip.srt", OneLineSrt);
        var catalogId = await catalogs.CreateAsync("Catalog", null);
        await catalogs.SetMemberAsync(catalogId, mediaId, true);

        await catalogs.DeleteAsync(catalogId);

        await using var context = await factory.CreateDbContextAsync();
        Assert.False(await context.Catalogs.AnyAsync(c => c.Id == catalogId));
        Assert.False(await context.CatalogMedia.AnyAsync(cm => cm.CatalogId == catalogId));
        Assert.True(await context.Media.AnyAsync(m => m.Id == mediaId));
    }

    [Fact]
    public async Task ApplyCatalogScopeAsync_SelectsExactlyTheCatalogsMembers()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (catalogs, library, factory) = setup.Value;

        var memberId = await ImportAsync(factory, _tempDir, "a.srt", OneLineSrt);
        var nonMemberId = await ImportAsync(factory, _tempDir, "b.srt", """
            1
            00:00:01,000 --> 00:00:02,000
            a long bus

            """);
        var catalogId = await catalogs.CreateAsync("Catalog", null);
        await catalogs.SetMemberAsync(catalogId, memberId, true);

        await library.ApplyCatalogScopeAsync([memberId], "Catalog");

        var (selectedIds, total) = await library.GetSelectionSummaryAsync();
        Assert.Equal(2, total);
        Assert.Equal([memberId], selectedIds);
        Assert.DoesNotContain(nonMemberId, selectedIds);
        Assert.Equal("Catalog", library.ActiveCatalogLabel);
    }

    [Fact]
    public async Task ManualSelectionEdit_ClearsTheActiveCatalogLabel()
    {
        var setup = await TrySetUpAsync();
        if (setup is null)
        {
            return;
        }

        var (_, library, factory) = setup.Value;

        var mediaId = await ImportAsync(factory, _tempDir, "clip.srt", OneLineSrt);

        await library.ApplyCatalogScopeAsync([mediaId], "Catalog");
        Assert.Equal("Catalog", library.ActiveCatalogLabel);

        await library.SetSelectedAsync(mediaId, false);
        Assert.Null(library.ActiveCatalogLabel);
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
