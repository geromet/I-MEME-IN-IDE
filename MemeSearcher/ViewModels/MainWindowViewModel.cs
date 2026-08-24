using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Shell hosting the top-level panels (handoff §43, Milestone 12's IDE shell). Library and Settings
/// stay single instances - Library is a persistent left panel now rather than a tab, and Settings
/// opens as its own window (#19 will make it a registered Tool instead). Search is the one panel
/// that needs multiple simultaneous instances: each executed search is its own document/tab, so two
/// queries can be compared side by side without one overwriting the other's results.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Func<SearchViewModel> _searchViewModelFactory;

    public LibraryViewModel Library { get; }

    public SettingsViewModel Settings { get; }

    public JobsPanelViewModel Jobs { get; }

    public ObservableCollection<SearchViewModel> SearchTabs { get; } = [];

    [ObservableProperty]
    private SearchViewModel? _activeSearchTab;

    /// <summary>
    /// Whether the bottom Jobs/Errors panel (Milestone 14) is shown - collapsible so the layout
    /// doesn't waste vertical space on it when there's nothing to show.
    /// </summary>
    [ObservableProperty]
    private bool _isBottomPanelVisible;

    public MainWindowViewModel(
        Func<SearchViewModel> searchViewModelFactory, LibraryViewModel library, SettingsViewModel settings, JobsPanelViewModel jobs)
    {
        _searchViewModelFactory = searchViewModelFactory;
        Library = library;
        Settings = settings;
        Jobs = jobs;

        // Milestone 13: an open search tab's scope indicator must reflect a checkbox toggled in
        // the (always-visible) library panel *before* the next search runs, not only after -
        // otherwise "no matches" from an unnoticed scope filter is indistinguishable from a query
        // that genuinely has none, which is exactly what the indicator exists to prevent. Owned
        // here (not by SearchViewModel depending on LibraryViewModel directly) because this is the
        // one place that already holds both sides without adding new coupling to either.
        Library.PropertyChanged += OnLibrarySelectionChanged;

        NewSearchTab();
    }

    private void OnLibrarySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryViewModel.SelectionSummary))
        {
            return;
        }

        foreach (var tab in SearchTabs)
        {
            _ = tab.RefreshScopeSummaryAsync();
        }
    }

    [RelayCommand]
    private void NewSearchTab()
    {
        var tab = _searchViewModelFactory();
        SearchTabs.Add(tab);
        ActiveSearchTab = tab;
    }

    /// <summary>
    /// Always leaves at least one tab open - closing the last one opens a fresh blank tab rather
    /// than leaving the document area empty with no way back in short of the toolbar.
    /// </summary>
    [RelayCommand]
    private void CloseSearchTab(SearchViewModel tab)
    {
        var index = SearchTabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        SearchTabs.RemoveAt(index);

        if (SearchTabs.Count == 0)
        {
            NewSearchTab();
            return;
        }

        if (ReferenceEquals(ActiveSearchTab, tab))
        {
            ActiveSearchTab = SearchTabs[Math.Min(index, SearchTabs.Count - 1)];
        }
    }

    [RelayCommand]
    private void ToggleBottomPanel() => IsBottomPanelVisible = !IsBottomPanelVisible;
}
