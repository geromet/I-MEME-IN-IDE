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

    /// <summary>#26: driven by the same search-selection signals as Inspector - one click, one meaning, fanned out to both panels.</summary>
    public TranscriptPanelViewModel TranscriptPanel { get; }

    public IReadOnlyList<PanelSlotViewModel> Panels { get; }

    public ObservableCollection<PanelSlotViewModel> LeftPanels { get; } = [];
    public ObservableCollection<PanelSlotViewModel> RightPanels { get; } = [];
    public ObservableCollection<PanelSlotViewModel> BottomPanels { get; } = [];

    [ObservableProperty] private bool _hasLeftPanels;
    [ObservableProperty] private bool _hasRightPanels;
    [ObservableProperty] private bool _hasBottomPanels;

    public ObservableCollection<SearchViewModel> SearchTabs { get; } = [];
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
                    RefreshZoneMembership(slot, zone);
            };
            if (!slot.IsVisible)
                zone.Remove(slot);
        }

        WireZoneCount(LeftPanels, v => HasLeftPanels = v);
        WireZoneCount(RightPanels, v => HasRightPanels = v);
        WireZoneCount(BottomPanels, v => HasBottomPanels = v);

        TranscriptPanel.SeedSearchRequested += OnSeedSearchRequested;
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

    private void RefreshZoneMembership(PanelSlotViewModel slot, ObservableCollection<PanelSlotViewModel> zone)
    {
        if (slot.IsVisible && !zone.Contains(slot))
            zone.Add(slot);
        else if (!slot.IsVisible)
            zone.Remove(slot);
    }

    private void OnLibrarySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryViewModel.SelectionSummary))
            return;

        foreach (var tab in SearchTabs)
            _ = tab.RefreshScopeSummaryAsync();
    }

    partial void OnActiveSearchTabChanged(SearchViewModel? value)
    {
        if (_inspectedTab is not null)
            _inspectedTab.PropertyChanged -= OnActiveTabPropertyChanged;

        _inspectedTab = value;

        if (_inspectedTab is not null)
            _inspectedTab.PropertyChanged += OnActiveTabPropertyChanged;

        ShowActiveInspectorSelection();
        TranscriptPanel.Show(value?.SelectedResult);
        TranscriptPanel.ShowComponent(value?.SelectedComponent);
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.SelectedResult)
            || e.PropertyName == nameof(SearchViewModel.SelectedCompositeResult))
        {
            ShowActiveInspectorSelection();
            if (e.PropertyName == nameof(SearchViewModel.SelectedResult))
                TranscriptPanel.Show(_inspectedTab?.SelectedResult);
        }
        else if (e.PropertyName == nameof(SearchViewModel.SelectedComponent))
        {
            TranscriptPanel.ShowComponent(_inspectedTab?.SelectedComponent);
        }
    }

    /// <summary>#35: composite result selection owns the Inspector when present; single-result behavior otherwise remains unchanged.</summary>
    private void ShowActiveInspectorSelection()
    {
        if (_inspectedTab?.SelectedCompositeResult is { } composite)
            Inspector.ShowComposite(composite);
        else
            Inspector.Show(_inspectedTab?.SelectedResult);
    }

    private void OnSeedSearchRequested(object? sender, string queryText)
    {
        if (ActiveSearchTab is not { } tab)
            return;

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

    [RelayCommand]
    private void CloseSearchTab(SearchViewModel tab)
    {
        var index = SearchTabs.IndexOf(tab);
        if (index < 0)
            return;

        SearchTabs.RemoveAt(index);

        if (SearchTabs.Count == 0)
        {
            NewSearchTab();
            return;
        }

        if (ReferenceEquals(ActiveSearchTab, tab))
            ActiveSearchTab = SearchTabs[Math.Min(index, SearchTabs.Count - 1)];
    }

    [RelayCommand]
    private void TogglePanel(PanelSlotViewModel slot) => slot.IsVisible = !slot.IsVisible;

    [RelayCommand]
    private void ToggleJobsPanel() => TogglePanel(Panels.First(p => p.Id == PanelIds.Jobs));
}
