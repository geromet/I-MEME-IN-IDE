namespace MemeSearcher.Core.Models;

/// <summary>
/// Catalog membership join row (#20). Both FKs cascade-delete (see MemeSearcherDbContext): removing
/// a source from the library removes it from every catalog without orphaning rows, and deleting a
/// catalog only ever removes these join rows, never the Media it pointed at.
/// </summary>
public class CatalogMedia
{
    public Guid CatalogId { get; set; }
    public Guid MediaId { get; set; }
}
