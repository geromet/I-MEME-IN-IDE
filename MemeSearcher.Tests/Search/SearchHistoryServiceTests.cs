using MemeSearcher.Infrastructure.Database;
using MemeSearcher.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemeSearcher.Tests.Search;

public class SearchHistoryServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"memesearcher-history-test-{Guid.NewGuid():N}.db");

    private async Task<IDbContextFactory<MemeSearcherDbContext>> CreateFactoryAsync()
    {
        var factory = new ServiceCollection()
            .AddDbContextFactory<MemeSearcherDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"))
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MemeSearcherDbContext>>();

        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return factory;
    }

    [Fact]
    public async Task RecordAsync_ThenGetRecentAsync_ReturnsNewestFirst()
    {
        var factory = await CreateFactoryAsync();
        var service = new SearchHistoryService(factory);

        await service.RecordAsync("among us", "en-US", isComposite: false, "All indexed media", resultCount: 3);
        await Task.Delay(10); // Ensure a distinguishable SearchedAt ordering, not relying on same-tick timestamps.
        await service.RecordAsync("a long bus", "en-US", isComposite: true, "Selected media", resultCount: 1);

        var recent = await service.GetRecentAsync();

        Assert.Equal(2, recent.Count);
        Assert.Equal("a long bus", recent[0].QueryText);
        Assert.True(recent[0].IsComposite);
        Assert.Equal("Selected media", recent[0].ScopeDescription);
        Assert.Equal(1, recent[0].ResultCount);
        Assert.Equal("among us", recent[1].QueryText);
        Assert.False(recent[1].IsComposite);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsTheRequestedCount()
    {
        var factory = await CreateFactoryAsync();
        var service = new SearchHistoryService(factory);

        for (var i = 0; i < 5; i++)
        {
            await service.RecordAsync($"query {i}", "en-US", isComposite: false, "All indexed media", resultCount: 0);
        }

        var recent = await service.GetRecentAsync(count: 3);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task GetRecentAsync_WithNoHistoryReturnsEmpty()
    {
        var factory = await CreateFactoryAsync();
        var service = new SearchHistoryService(factory);

        var recent = await service.GetRecentAsync();

        Assert.Empty(recent);
    }

    public void Dispose()
    {
        File.Delete(_dbPath);
        File.Delete(_dbPath + "-shm");
        File.Delete(_dbPath + "-wal");
    }
}
