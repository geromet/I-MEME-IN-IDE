using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MemeSearcher.ViewModels;

/// <summary>One row of the membership editor for the currently-selected catalog - every media item in the library, with a checkbox for whether it belongs to this catalog.</summary>
public partial class CatalogMemberRowViewModel(Guid mediaId, string title, bool isMember) : ObservableObject
{
    public Guid MediaId { get; } = mediaId;
    public string Title { get; } = title;

    [ObservableProperty]
    private bool _isMember = isMember;
}
