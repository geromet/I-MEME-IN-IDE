using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MemeSearcher.Infrastructure.Catalogs;

namespace MemeSearcher.ViewModels;

public partial class CatalogRowViewModel(CatalogSummary summary) : ObservableObject
{
    public Guid Id { get; } = summary.Id;
    public string Name { get; } = summary.Name;
    public string? Description { get; } = summary.Description;
    public bool HasDescription { get; } = !string.IsNullOrWhiteSpace(summary.Description);
    public string MemberCountDisplay { get; } = $"{summary.MemberCount} source(s)";

    // Two-step confirmation for delete, same pattern as MediaRowViewModel.IsPendingDelete.
    [ObservableProperty]
    private bool _isPendingDelete;
}
