using MemeSearcher.Core.Search;

namespace MemeSearcher.Infrastructure.Templates;

public record TemplateVariantSummary(Guid Id, string Label, string PhonesRaw, MemeSearcher.Core.Phonetics.PhoneAlphabet Alphabet, int Sequence);

/// <summary>Read projection for the Templates view (#21/#36) - a template plus its variants and persisted search options in one shape, since those authoring controls are edited together.</summary>
public record TemplateSummary(
    Guid Id,
    string Name,
    string? Description,
    SearchMode Mode,
    Guid? TargetCatalogId,
    string? SearchOptionsJson,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TemplateVariantSummary> Variants);
