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

    /// <summary>Milestone 18 (#21): a template run has no text/language to show, and SearchViewModel's rerun path assumes both are present - the two kinds of entry must never mix in the same list.</summary>
    [Fact]
    public async Task RecordTemplateRunAsync_DoesNotAppearInGetRecentAsync_OnlyInItsOwnList()
    {
        var factory = await CreateFactoryAsync();
        var service = new SearchHistoryService(factory);
        var templateId = await new MemeSearcher.Infrastructure.Templates.TemplateService(factory).CreateAsync("Growl", null);

        await service.RecordAsync("among us", "en-US", isComposite: false, "All indexed media", resultCount: 3);
        await service.RecordTemplateRunAsync(templateId, "Growl", "All indexed media", resultCount: 1);

        var textHistory = await service.GetRecentAsync();
        var templateRuns = await service.GetRecentTemplateRunsAsync();

        var textEntry = Assert.Single(textHistory);
        Assert.Equal("among us", textEntry.QueryText);
        Assert.Null(textEntry.TemplateId);

        var templateEntry = Assert.Single(templateRuns);
        Assert.Equal(templateId, templateEntry.TemplateId);
        Assert.Equal("Growl", templateEntry.TemplateName);
        Assert.Null(templateEntry.QueryText);
        Assert.Null(templateEntry.Language);
        Assert.Equal(1, templateEntry.ResultCount);
    }

    [Fact]
    public async Task RecordTemplateRunAsync_WithSelectedMediaIds_PersistsThemForToSearchScope()
    {
        var factory = await CreateFactoryAsync();
        var service = new SearchHistoryService(factory);
        var templateId = await new MemeSearcher.Infrastructure.Templates.TemplateService(factory).CreateAsync("Growl", null);

        var mediaId = Guid.NewGuid();
        await service.RecordTemplateRunAsync(templateId, "Growl", "Catalog: Growls only (1 source(s))", resultCount: 1, [mediaId]);

        var entry = Assert.Single(await service.GetRecentTemplateRunsAsync());
        var scope = entry.ToSearchScope();
        var selected = Assert.IsType<MemeSearcher.Core.Search.SearchScope.SelectedMedia>(scope);
        Assert.Equal([mediaId], selected.MediaIds);
    }

    public void Dispose()
    {
        File.Delete(_dbPath);
        File.Delete(_dbPath + "-shm");
        File.Delete(_dbPath + "-wal");
    }
}
