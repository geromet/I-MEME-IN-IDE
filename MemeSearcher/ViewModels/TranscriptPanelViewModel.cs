using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Transcription;

namespace MemeSearcher.ViewModels;

/// <summary>
/// Backs the transcript viewer panel (#26, Milestone 21): one tab per media a search result has
/// been opened from, scrolled and highlighted to the selected result's own matched cue(s).
///
/// A registered IViewPanel (per #19 - the issue's own instruction not to hardcode this into shell
/// XAML), but the tab strip inside it is this panel's own private concern, the same way
/// MainWindowViewModel's SearchTabs is a document-area concept independent of the dock-zone panel
/// framework - a transcript tab is not itself a second IViewPanel.
///
/// Driven by the same SelectedResult signal that already drives the Inspector (MainWindowViewModel
/// calls both from one place) rather than inventing a second "click means open the transcript"
/// concept - one click, one meaning, fanned out to two panels (#26's own explicit concern, raised
/// alongside #25).
/// </summary>
public partial class TranscriptPanelViewModel(TranscriptViewService transcriptViewService, LibraryService libraryService) : ViewModelBase
{
    public ObservableCollection<TranscriptTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private TranscriptTabViewModel? _activeTab;

    public bool HasTabs => Tabs.Count > 0;

    /// <summary>
    /// Opens (or focuses, if already open) the tab for this result's media, and highlights its
    /// matched cue(s). A null result (Inspector's "nothing selected" signal) is deliberately a
    /// no-op here rather than closing every open tab - unlike the Inspector, which clears to a
    /// blank state, a transcript the user was reading stays open until they close it themselves;
    /// losing your place because the result list happened to deselect would be actively hostile.
    /// </summary>
    public void Show(SearchResultRowViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        _ = ShowAsync(result);
    }

    /// <summary>The awaitable core of Show(...), exposed separately so tests can await the actual database/lookup work deterministically instead of racing the fire-and-forget call Show makes for UI binding.</summary>
    public async Task ShowAsync(SearchResultRowViewModel result)
    {
        var tab = Tabs.FirstOrDefault(t => t.MediaId == result.MediaId);

        if (tab is null)
        {
            var cues = await transcriptViewService.GetCuesAsync(result.MediaId);
            if (cues is null)
            {
                // No transcript at all for this media - nothing to open. Shouldn't happen for a
                // result that itself came from searching that media's transcript, but a result
                // selected after the underlying media was removed is a real, if rare, race.
                return;
            }

            var titles = await libraryService.GetTitlesAsync([result.MediaId]);
            var title = titles.GetValueOrDefault(result.MediaId, result.MediaId.ToString()[..8]);

            tab = new TranscriptTabViewModel(result.MediaId, title, cues);
            Tabs.Add(tab);
            OnPropertyChanged(nameof(HasTabs));
        }

        ActiveTab = tab;

        var segmentIds = result.MatchedPhoneDetails
            .Where(p => p.SegmentId is not null)
            .Select(p => p.SegmentId!.Value)
            .ToHashSet();

        if (segmentIds.Count > 0)
        {
            tab.HighlightSegments(segmentIds);
        }
        else
        {
            // A result with no per-phone provenance at all (shouldn't happen via the normal search
            // path, but defensive rather than leaving a stale highlight from a previous selection).
            tab.ClearHighlight();
        }
    }

    [RelayCommand]
    private void SelectTab(TranscriptTabViewModel tab) => ActiveTab = tab;

    [RelayCommand]
    private void CloseTab(TranscriptTabViewModel tab)
    {
        Tabs.Remove(tab);
        OnPropertyChanged(nameof(HasTabs));

        if (ActiveTab == tab)
        {
            ActiveTab = Tabs.LastOrDefault();
        }
    }
}
