using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MemeSearcher.ViewModels;

/// <summary>
/// #35 composite-Inspector selection wiring. Kept beside the existing SearchViewModel rather than
/// adding another search/result model: composite rows already contain the provenance Inspector needs.
/// </summary>
public partial class SearchViewModel
{
    [ObservableProperty]
    private CompositeSearchResultRowViewModel? _selectedCompositeResult;

    partial void OnSelectedComponentChanged(CompositeComponentRowViewModel? value)
    {
        if (value is null)
            return;

        SelectedCompositeResult = CompositeResults.FirstOrDefault(result => result.Components.Contains(value));
    }

    partial void OnSelectedResultChanged(SearchResultRowViewModel? value)
    {
        if (value is not null)
        {
            SelectedCompositeResult = null;
            SelectedComponent = null;
        }
    }

    partial void OnSelectedCompositeResultChanged(CompositeSearchResultRowViewModel? value)
    {
        if (value is not null)
            SelectedResult = null;
    }
}
