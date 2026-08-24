using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MemeSearcher.ViewModels;

/// <summary>
/// #25 exit criterion 3: one query span the user is assembling a clip for, with the candidates
/// available to fill it - the same rows ResultGrouping.GroupByCoveredSpan already collected for that
/// span. A dedicated model rather than reusing CompositeMatchComponent: a slot is a work-in-progress
/// choice among several competing candidates (including "none picked yet"), which
/// CompositeMatchComponent's shape - one resolved component, chosen by the matcher - has no room for.
/// </summary>
public partial class AssemblySlotViewModel(string label, int queryStart, int queryEnd, IReadOnlyList<SearchResultRowViewModel> candidates)
    : ObservableObject
{
    public string Label { get; } = label;

    public int QueryStart { get; } = queryStart;

    public int QueryEnd { get; } = queryEnd;

    public IReadOnlyList<SearchResultRowViewModel> Candidates { get; } = candidates;

    /// <summary>Defaults to the top-ranked candidate (Candidates arrives already sorted by ResultSortFilter) rather than unfilled, so a fresh draft is audition-ready immediately and the user only has to change slots they disagree with.</summary>
    [ObservableProperty]
    private SearchResultRowViewModel? _selectedCandidate = candidates.FirstOrDefault();

    /// <summary>Leaves this span out of the assembly entirely - a valid choice (AssemblyDraftViewModel.IsComplete only needs one slot filled), not an error state to route around.</summary>
    [RelayCommand]
    private void Skip() => SelectedCandidate = null;
}
