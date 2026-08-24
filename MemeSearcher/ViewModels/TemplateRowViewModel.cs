using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Templates;

namespace MemeSearcher.ViewModels;

public partial class TemplateRowViewModel(TemplateSummary summary) : ObservableObject
{
    public Guid Id { get; } = summary.Id;
    public string Name { get; } = summary.Name;
    public string? Description { get; } = summary.Description;
    public bool HasDescription { get; } = !string.IsNullOrWhiteSpace(summary.Description);
    public SearchMode Mode { get; } = summary.Mode;
    public Guid? TargetCatalogId { get; } = summary.TargetCatalogId;
    public string VariantCountDisplay { get; } =
        summary.Variants.Count == 1 ? "1 variant" : $"{summary.Variants.Count} variants";

    [ObservableProperty]
    private bool _isPendingDelete;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = summary.Name;

    [ObservableProperty]
    private string _editDescription = summary.Description ?? "";
}
