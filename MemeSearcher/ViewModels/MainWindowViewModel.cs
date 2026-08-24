using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Shell;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Shell hosting the top-level panels (handoff §43, Milestone 12's IDE shell; #19 replaced the
/// hardcoded Library/Inspector/Jobs/Settings panels with registered <see cref="IViewPanel"/>s in
/// fixed dock zones - this is the one mechanism, not a framework plus special cases). Search is the
/// one panel that needs multiple simultaneous instances: each executed search is its own
/// document/tab, so two queries can be compared side by side without one overwriting the other's
/// results.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Func<SearchViewModel> _searchViewModelFactory;

    public LibraryViewModel Library { get; }

    public JobsPanelViewModel Jobs { get; }

    public InspectorViewModel Inspector { get; }

    /// <summary>#26: driven by the same SelectedResult signal as Inspector - one click, one meaning, fanned out to both panels.</summary>
    public TranscriptPanelViewModel TranscriptPanel { get; }

    /// <summary>Every registered panel, in registration order - what the View menu lists.</summary>
    public IReadOnlyList<PanelSlotViewModel> Panels { get; }

    public ObservableCollection<PanelSlotViewModel> LeftPanels { get; } = [];

    public ObservableCollection<PanelSlotViewModel> RightPanels { get; } = [];

    public ObservableCollection<PanelSlotViewModel> BottomPanels { get; } = [];

    /// <summary>A zone's chrome (border, splitter) is only shown while it has at least one visible panel - hiding a zone's last panel collapses the space rather than leaving an empty pane.</summary>
    [ObservableProperty]
    private bool _hasLeftPanels;

    [ObservableProperty]
    private bool _hasRightPanels;

    [ObservableProperty]
    private bool _hasBottomPanels;

    public ObservableCollection<SearchViewModel> SearchTabs { get; } = [];

    /// <summary>The tab Inspector is currently subscribed to, so switching tabs can unsubscribe the old one before subscribing the new one (#15).</summary>
    private SearchViewModel? _inspectedTab;

    [ObservableProperty]
    private SearchViewModel? _activeSearchTab;

    public MainWindowViewModel(
        Func<SearchViewModel> searchViewModelFactory, LibraryViewModel library,
        JobsPanelViewModel jobs, InspectorViewModel inspector, TranscriptPanelViewModel transcriptPanel,
        IEnumerable<IViewPanel> panels, Core.Settings.ISettingsStore settingsStore)
    {
        _searchViewModelFactory = searchViewModelFactory;
        Library = library;
        Jobs = jobs;
        Inspector = inspector;
        TranscriptPanel = transcriptPanel;

        Panels = panels.Select(p => new PanelSlotViewModel(p, settingsStore)).ToArray();
        foreach (var slot in Panels)
        {
            var zone = ZoneFor(slot.Dock);
            zone.Add(slot);
            slot.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PanelSlotViewModel.IsVisible))
                {
                    RefreshZoneMembership(slot, zone);
                }
            };
            if (!slot.IsVisible)
            {
                zone.Remove(slot);
            }
        }

        WireZoneCount(LeftPanels, v => HasLeftPanels = v);
        WireZoneCount(RightPanels, v => HasRightPanels = v);
        WireZoneCount(BottomPanels, v => HasBottomPanels = v);

        // #26 part 3: the transcript panel doesn't know which search tab is active, so it just
        // raises a plain event when a word/cue is clicked - this is the one place that already
        // owns both ends (ActiveSearchTab and TranscriptPanel) to complete the loop.
        TranscriptPanel.SeedSearchRequested += OnSeedSearchRequested;

        // Milestone 13: an open search tab's scope indicator must reflect a checkbox toggled in
        // the (always-visible) library panel *before* the next search runs, not only after -
        // otherwise "no matches" from an unnoticed scope filter is indistinguishable from a query
        // that genuinely has none, which is exactly what the indicator exists to prevent. Owned
        // here (not by SearchViewModel depending on LibraryViewModel directly) because this is the
        // one place that already holds both sides without adding new coupling to either.
        Library.PropertyChanged += OnLibrarySelectionChanged;

        NewSearchTab();
    }

    private static void WireZoneCount(ObservableCollection<PanelSlotViewModel> zone, Action<bool> setHasPanels)
    {
        zone.CollectionChanged += (_, _) => setHasPanels(zone.Count > 0);
        setHasPanels(zone.Count > 0);
    }

    private ObservableCollection<PanelSlotViewModel> ZoneFor(DockZone zone) => zone switch
    {
        DockZone.Left => LeftPanels,
        DockZone.Right => RightPanels,
        DockZone.Bottom => BottomPanels,
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };

    /// <summary>A zone's TabControl only shows visible panels - a hidden one drops out of the collection entirely rather than rendering an empty/disabled tab.</summary>
    private void RefreshZoneMembership(PanelSlotViewModel slot, ObservableCollection<PanelSlotViewModel> zone)
    {
        if (slot.IsVisible && !zone.Contains(slot))
        {
            zone.Add(slot);
        }
        else if (!slot.IsVisible)
        {
            zone.Remove(slot);
        }
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

    /// <summary>
    /// Milestone 15 (#15): the shared Inspector panel shows whichever result is selected in the
    /// *active* tab - so it has to re-subscribe every time the active tab changes, not just once.
    /// </summary>
    partial void OnActiveSearchTabChanged(SearchViewModel? value)
    {
        if (_inspectedTab is not null)
        {
            _inspectedTab.PropertyChanged -= OnActiveTabPropertyChanged;
        }

        _inspectedTab = value;

        if (_inspectedTab is not null)
        {
            _inspectedTab.PropertyChanged += OnActiveTabPropertyChanged;
        }

        Inspector.Show(value?.SelectedResult);
        TranscriptPanel.Show(value?.SelectedResult);
        TranscriptPanel.ShowComponent(value?.SelectedComponent);
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.SelectedResult))
        {
            Inspector.Show(_inspectedTab?.SelectedResult);
            TranscriptPanel.Show(_inspectedTab?.SelectedResult);
        }
        else if (e.PropertyName == nameof(SearchViewModel.SelectedComponent))
        {
            // #26 part 3: composite mode's own click signal, wired the same way SelectedResult is -
            // one click, one meaning, this time focusing just the clicked component's own media.
            TranscriptPanel.ShowComponent(_inspectedTab?.SelectedComponent);
        }
    }

    /// <summary>#26 part 3: sets the active tab's query to the clicked word/cue text and runs it - the same "set QueryText then search" shape RerunSearchAsync already uses for history entries.</summary>
    private void OnSeedSearchRequested(object? sender, string queryText)
    {
        if (ActiveSearchTab is not { } tab)
        {
            return;
        }

        tab.QueryText = queryText;
        tab.SearchCommand.Execute(null);
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
    private void TogglePanel(PanelSlotViewModel slot) => slot.IsVisible = !slot.IsVisible;

    [RelayCommand]
    private void ToggleJobsPanel() => TogglePanel(Panels.First(p => p.Id == PanelIds.Jobs));
}
