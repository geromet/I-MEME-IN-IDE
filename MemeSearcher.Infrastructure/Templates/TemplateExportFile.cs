namespace MemeSearcher.Infrastructure.Templates;

/// <summary>
/// On-disk shape for template export/import (#21). Deliberately excludes TargetCatalogId - a
/// catalog is a local id with no meaning in whatever database the file is imported into, so an
/// imported template always starts scoped to "all indexed media" rather than pointing at
/// nothing/the wrong catalog. Enums are written as names, not ordinals, so the file stays readable
/// (and importable) if either enum's member order ever changes.
/// </summary>
public record TemplateExportVariant(string Label, string PhonesRaw, string Alphabet);

public record TemplateExportEntry(string Name, string? Description, string Mode, string? SearchOptionsJson, IReadOnlyList<TemplateExportVariant> Variants);

/// <summary>The file itself is a JSON array of entries, not a single object - so a shared file can bundle a curated set of templates, not just one.</summary>
public record TemplateExportFile(IReadOnlyList<TemplateExportEntry> Templates);
