using MemeSearcher.Core.Models;
using MemeSearcher.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace MemeSearcher.Infrastructure.Catalogs;

/// <summary>
/// CRUD and membership management for catalogs (#20) - named, saved, curated subsets of the
/// corpus. Deliberately doesn't touch Media.IsSelectedForSearch itself; applying a catalog as the
/// active search scope is LibraryService's job (LibraryService.ApplyCatalogScopeAsync), since that
/// flag is what #13's scope machinery already reads and #20 explicitly asks for no new scope
/// machinery in Core.
/// </summary>
public class CatalogService(IDbContextFactory<MemeSearcherDbContext> dbContextFactory)
{
    public async Task<List<CatalogSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var catalogs = (await context.Catalogs.ToListAsync(cancellationToken))
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        var counts = await context.CatalogMedia
            .GroupBy(cm => cm.CatalogId)
            .Select(g => new { CatalogId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CatalogId, x => x.Count, cancellationToken);

        return catalogs
            .Select(c => new CatalogSummary(c.Id, c.Name, c.Description, counts.GetValueOrDefault(c.Id), c.CreatedAt))
            .ToList();
    }

    public async Task<Guid> CreateAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var catalog = new Catalog
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.Catalogs.Add(catalog);
        await context.SaveChangesAsync(cancellationToken);
        return catalog.Id;
    }

    public async Task RenameAsync(Guid catalogId, string name, string? description, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var catalog = await context.Catalogs.FindAsync([catalogId], cancellationToken);
        if (catalog is null)
        {
            return;
        }

        catalog.Name = name;
        catalog.Description = description;
        catalog.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid catalogId, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var catalog = await context.Catalogs.FindAsync([catalogId], cancellationToken);
        if (catalog is null)
        {
            return;
        }

        // CatalogMedia rows cascade-delete via the FK configured in MemeSearcherDbContext - the
        // Media rows they pointed at are never touched (#20: deleting a catalog must never delete sources).
        context.Catalogs.Remove(catalog);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<HashSet<Guid>> GetMemberIdsAsync(Guid catalogId, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ids = await context.CatalogMedia
            .Where(cm => cm.CatalogId == catalogId)
            .Select(cm => cm.MediaId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task SetMemberAsync(Guid catalogId, Guid mediaId, bool isMember, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.CatalogMedia.FindAsync([catalogId, mediaId], cancellationToken);

        if (isMember && existing is null)
        {
            context.CatalogMedia.Add(new CatalogMedia { CatalogId = catalogId, MediaId = mediaId });
        }
        else if (!isMember && existing is not null)
        {
            context.CatalogMedia.Remove(existing);
        }
        else
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
