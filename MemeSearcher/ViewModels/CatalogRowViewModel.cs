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

    /// <summary>Whether this row is showing the rename form instead of its normal display. Reset on the next CatalogsViewModel.LoadAsync, same as IsPendingDelete above - acceptable since a reload only happens after a create/delete/membership edit elsewhere, not while renaming.</summary>
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = summary.Name;

    [ObservableProperty]
    private string _editDescription = summary.Description ?? "";
}
