using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Processes;
using MemeSearcher.Infrastructure.YtDlp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.YtDlp;

/// <summary>
/// Real database, canned YtDlpVideoEntry list rather than a live yt-dlp/network enumeration -
/// ClassifyAsync exists specifically so this can be tested without depending on either (#27).
/// </summary>
public class YtDlpImportPlannerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-ytdlpplanner-test-{Guid.NewGuid():N}.db");

    private async Task<IDbContextFactory<MemeSearcherDbContext>> SetUpAsync()
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return factory;
    }

    private static Media MakeImportedMedia(string videoId) => new()
    {
        Id = Guid.NewGuid(),
        Path = $"/tmp/{videoId}.mp4",
        Language = "en-US",
        ContentHash = Guid.NewGuid().ToString("N"),
        VideoId = videoId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static YtDlpImportFailure MakeFailure(string videoId) => new()
    {
        Id = Guid.NewGuid(),
        VideoId = videoId,
        SourceUrl = $"https://www.youtube.com/watch?v={videoId}",
        Reason = "Private video",
        FailedAt = DateTimeOffset.UtcNow,
        AttemptCount = 1,
    };

    [Fact]
    public async Task ClassifyAsync_SortsEntriesIntoNewAlreadyImportedAndPreviouslyFailed()
    {
        var factory = await SetUpAsync();

        await using (var context = await factory.CreateDbContextAsync())
        {
            context.Media.Add(MakeImportedMedia("already-imported-id"));
            context.YtDlpImportFailures.Add(MakeFailure("previously-failed-id"));
            await context.SaveChangesAsync();
        }

        var entries = new[]
        {
            new YtDlpVideoEntry("new-id", "A brand new video", "Some Channel", "https://www.youtube.com/watch?v=new-id"),
            new YtDlpVideoEntry("already-imported-id", "Already have this one", "Some Channel", "https://www.youtube.com/watch?v=already-imported-id"),
            new YtDlpVideoEntry("previously-failed-id", "This one failed before", "Some Channel", "https://www.youtube.com/watch?v=previously-failed-id"),
        };

        var planner = new YtDlpImportPlanner(
            new YtDlpPlaylistEnumerationService(new YtDlpToolLocator()), // never called - ClassifyAsync bypasses enumeration entirely
            factory);
        var plan = await planner.ClassifyAsync(entries);

        Assert.Equal(3, plan.TotalCount);
        Assert.Equal(1, plan.NewCount);
        Assert.Equal(1, plan.AlreadyImportedCount);
        Assert.Equal(1, plan.PreviouslyFailedCount);

        Assert.Equal(YtDlpImportPlanStatus.New, plan.Items.Single(i => i.Entry.VideoId == "new-id").Status);
        Assert.Equal(YtDlpImportPlanStatus.AlreadyImported, plan.Items.Single(i => i.Entry.VideoId == "already-imported-id").Status);
        Assert.Equal(YtDlpImportPlanStatus.PreviouslyFailed, plan.Items.Single(i => i.Entry.VideoId == "previously-failed-id").Status);
    }

    [Fact]
    public async Task ClassifyAsync_EmptyCorpus_EverythingIsNew()
    {
        var factory = await SetUpAsync();
        var entries = new[] { new YtDlpVideoEntry("id1", "Video One", null, "https://www.youtube.com/watch?v=id1") };

        var planner = new YtDlpImportPlanner(new YtDlpPlaylistEnumerationService(new YtDlpToolLocator()), factory);
        var plan = await planner.ClassifyAsync(entries);

        Assert.Equal(1, plan.NewCount);
        Assert.Equal(0, plan.AlreadyImportedCount);
        Assert.Equal(0, plan.PreviouslyFailedCount);
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
