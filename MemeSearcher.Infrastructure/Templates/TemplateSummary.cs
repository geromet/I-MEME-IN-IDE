using MemeSearcher.Core.Search;

namespace MemeSearcher.Infrastructure.Templates;

public record TemplateVariantSummary(Guid Id, string Label, string PhonesRaw, MemeSearcher.Core.Phonetics.PhoneAlphabet Alphabet, int Sequence);

/// <summary>Read projection for the Templates view (#21) - a template plus its variants in one shape, since the two are never meaningfully shown apart.</summary>
public record TemplateSummary(
    Guid Id,
    string Name,
    string? Description,
    SearchMode Mode,
    Guid? TargetCatalogId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TemplateVariantSummary> Variants);
