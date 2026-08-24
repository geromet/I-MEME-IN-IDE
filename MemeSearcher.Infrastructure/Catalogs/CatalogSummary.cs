namespace MemeSearcher.Infrastructure.Catalogs;

/// <summary>Read projection for the Catalogs view (#20) - name/description plus a live member count, not the membership itself.</summary>
public record CatalogSummary(
    Guid Id,
    string Name,
    string? Description,
    int MemberCount,
    DateTimeOffset CreatedAt);
