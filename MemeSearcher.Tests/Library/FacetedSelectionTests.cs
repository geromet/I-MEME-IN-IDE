using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Library;

public class FacetedSelectionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"memesearcher-facets-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task GetSelectionSummaryAsync_IntersectsPersistentSelectionWithFacets()
    {
        var (library, factory) = await SetUpAsync();
        var alphaAudio = NewMedia(
            selected: true,
            channel: "Alpha Channel",
            language: "en-US",
            mediaKind: YtDlpMediaKind.Audio,
            uploadDate: new DateOnly(2026, 5, 10));
        var betaVideo = NewMedia(
            selected: true,
            channel: "Beta Channel",
            language: "en-US",
            mediaKind: YtDlpMediaKind.Video,
            uploadDate: new DateOnly(2026, 6, 10));
        var excludedAlpha = NewMedia(
            selected: false,
            channel: "Alpha Channel",
            language: "en-US",
            mediaKind: YtDlpMediaKind.Audio,
            uploadDate: new DateOnly(2026, 5, 12));

        await SeedAsync(factory, alphaAudio, betaVideo, excludedAlpha);

        var facets = new MediaSearchFacets
        {
            Channels = ["alpha channel"],
            Languages = ["EN-us"],
            MediaKinds = [YtDlpMediaKind.Audio],
            UploadedOnOrAfter = new DateOnly(2026, 5, 1),
            UploadedOnOrBefore = new DateOnly(2026, 5, 31),
        };

        var (selectedIds, total) = await library.GetSelectionSummaryAsync(facets);

        Assert.Equal(3, total);
        Assert.Equal([alphaAudio.Id], selectedIds);
    }

    [Fact]
    public async Task GetSelectionSummaryAsync_CanExplicitlyIncludeUnknownLocalSources()
    {
        var (library, factory) = await SetUpAsync();
        var local = NewMedia(
            selected: true,
            channel: null,
            language: "nl-NL",
            mediaKind: null,
            uploadDate: null);
        var youtube = NewMedia(
            selected: true,
            channel: "Some Channel",
            language: "nl-NL",
            mediaKind: YtDlpMediaKind.Video,
            uploadDate: new DateOnly(2026, 1, 1));

        await SeedAsync(factory, local, youtube);

        var facets = new MediaSearchFacets
        {
            IncludeUnknownChannel = true,
            IncludeNonYtDlpMedia = true,
            Languages = ["nl-NL"],
        };

        var (selectedIds, _) = await library.GetSelectionSummaryAsync(facets);

        Assert.Equal([local.Id], selectedIds);
    }

    [Fact]
    public void Matches_DateFacetExcludesSourcesWithoutAnUploadDate()
    {
        var local = NewMedia(
            selected: true,
            channel: null,
            language: "en-US",
            mediaKind: null,
            uploadDate: null);
        var facets = new MediaSearchFacets
        {
            UploadedOnOrAfter = new DateOnly(2026, 1, 1),
        };

        Assert.False(facets.Matches(local));
    }

    private async Task<(LibraryService Library, IDbContextFactory<MemeSearcherDbContext> Factory)> SetUpAsync()
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(options =>
                options.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return (new LibraryService(factory), factory);
    }

    private static async Task SeedAsync(
        IDbContextFactory<MemeSearcherDbContext> factory,
        params Media[] media)
    {
        await using var context = await factory.CreateDbContextAsync();
        context.Media.AddRange(media);
        await context.SaveChangesAsync();
    }

    private static Media NewMedia(
        bool selected,
        string? channel,
        string language,
        YtDlpMediaKind? mediaKind,
        DateOnly? uploadDate)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new Media
        {
            Id = id,
            Path = $"/tmp/{id:N}.media",
            Language = language,
            ContentHash = id.ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
            LastModified = now,
            IsSelectedForSearch = selected,
            Channel = channel,
            YtDlpMediaKind = mediaKind,
            UploadDate = uploadDate,
        };
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
    }
}
