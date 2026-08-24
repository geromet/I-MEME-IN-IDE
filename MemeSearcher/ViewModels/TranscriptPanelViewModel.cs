using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Search;
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
    /// Raised when the user clicks a matched word to seed a search from it (#26 part 3's "reverse
    /// direction" - the "seed-from-result" authoring loop #21 wants for phone templates, arriving
    /// from the transcript side). A plain string event rather than reaching back into
    /// MainWindowViewModel directly, since this panel doesn't know which search tab is active - that
    /// wiring belongs to whoever owns both ends of the fan-out, same as the SelectedResult signal
    /// travelling the other direction.
    ///
    /// Word-level only, deliberately: the individually-sized word buttons only exist in a cue whose
    /// highlight is already word-level (HasWordHighlights), i.e. media that's been realigned with
    /// real per-word timing. A whole-cue click target was tried and dropped - inside a ListBox row
    /// it's too easy to hit by accident while just reading, and a stray seeded search would blow
    /// away whatever result set or #25 assembly draft the user had open to get here. This means
    /// reverse-seeding isn't reachable from a plain (interpolated-timing) transcript - the same
    /// trade-off #26 part 2 already made for highlighting, applied to input as well as display.
    /// </summary>
    public event EventHandler<string>? SeedSearchRequested;

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
    public Task ShowAsync(SearchResultRowViewModel result) => ShowAsync(result.MediaId, result.MatchedPhoneDetails);

    /// <summary>
    /// Composite results have no single "the" result - a click focuses just the clicked component's
    /// own media rather than opening every contributing transcript (#26 part 3's own open question,
    /// resolved this way: it's the simpler behaviour and the issue itself flagged it as probably
    /// right). Same null-is-a-no-op rule as Show(...).
    /// </summary>
    public void ShowComponent(CompositeComponentRowViewModel? component)
    {
        if (component is null)
        {
            return;
        }

        _ = ShowAsync(component.MediaId, component.MatchedPhoneDetails);
    }

    /// <summary>The awaitable core of ShowComponent(...) - see ShowAsync(SearchResultRowViewModel)'s own doc comment for why this is exposed separately.</summary>
    public Task ShowComponentAsync(CompositeComponentRowViewModel component) => ShowAsync(component.MediaId, component.MatchedPhoneDetails);

    private async Task ShowAsync(Guid mediaId, IReadOnlyList<MatchedPhone> matchedPhoneDetails)
    {
        var tab = Tabs.FirstOrDefault(t => t.MediaId == mediaId);

        if (tab is null)
        {
            var cues = await transcriptViewService.GetCuesAsync(mediaId);
            if (cues is null)
            {
                // No transcript at all for this media - nothing to open. Shouldn't happen for a
                // result that itself came from searching that media's transcript, but a result
                // selected after the underlying media was removed is a real, if rare, race.
                return;
            }

            var titles = await libraryService.GetTitlesAsync([mediaId]);
            var title = titles.GetValueOrDefault(mediaId, mediaId.ToString()[..8]);

            tab = new TranscriptTabViewModel(mediaId, title, cues);
            Tabs.Add(tab);
            OnPropertyChanged(nameof(HasTabs));
        }

        ActiveTab = tab;

        var segmentIds = matchedPhoneDetails
            .Where(p => p.SegmentId is not null)
            .Select(p => p.SegmentId!.Value)
            .ToHashSet();

        if (segmentIds.Count > 0)
        {
            var wordIds = matchedPhoneDetails
                .Where(p => p.WordId is not null)
                .Select(p => p.WordId!.Value)
                .ToHashSet();
            tab.HighlightMatches(segmentIds, wordIds);
        }
        else
        {
            // No per-phone provenance at all (shouldn't happen via the normal search path, but
            // defensive rather than leaving a stale highlight from a previous selection).
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

    [RelayCommand]
    private void SeedSearchFromWord(TranscriptWordViewModel word) => SeedSearchRequested?.Invoke(this, word.Text);
}
