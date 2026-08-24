using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemeSearcher.Core.Interfaces;
using MemeSearcher.Core.Settings;
using MemeSearcher.Core.Models;
using MemeSearcher.Core.Search;
using MemeSearcher.Infrastructure.Ffmpeg;
using MemeSearcher.Infrastructure.Library;
using MemeSearcher.Infrastructure.Search;
using MemeSearcher.Infrastructure.Settings;
using MemeSearcher.Services;

namespace MemeSearcher.ViewModels;

public partial class SearchViewModel(
    IPhoneticSearchService searchService,
    ICompositeSearchService compositeSearchService,
    IPhonemizer phonemizer,
    IQueryPhonemizationCache queryCache,
    SearchHistoryService searchHistoryService,
    LibraryService libraryService,
    IMediaPlayerLauncher playerLauncher,
    IClipboardService clipboard,
    FFmpegClipExtractor clipExtractor,
    IFilePickerService filePicker,
    ISettingsStore settings) : ViewModelBase
{
    // Read from settings, not a constant (#24). A query must be phonemized in the same language
    // the corpus was ingested with, and both this and LibraryViewModel read the same setting - so
    // they cannot drift the way two separate "en-US" constants did (#23).
    private string Language => settings.Get(WhisperXSettings.Language);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _queryText = "";

    [ObservableProperty]
    private string _queryIpa = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Import media in the Library tab, then search.";

    // Milestone 4: single-source is the default per addendum §17 - composite must be opt-in.
    [ObservableProperty]
    private bool _isCompositeMode;

    /// <summary>
    /// Addendum §13: what a search will actually run against, shown next to the query so an
    /// unnoticed scope filter is never mistaken for "genuinely no matches". Refreshed when this
    /// tab is opened (SearchView's DataContextChanged) and after every search actually runs, so it
    /// never drifts from what the last/next search will really see.
    /// </summary>
    [ObservableProperty]
    private string _scopeSummary = "";

    /// <summary>
    /// Addendum §25: offered only after a scoped search comes back empty - searching the full
    /// corpus by default would defeat the point of scoping at all.
    /// </summary>
    [ObservableProperty]
    private bool _canWidenToFullCorpus;

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];

    public ObservableCollection<CompositeSearchResultRowViewModel> CompositeResults { get; } = [];

    /// <summary>#25: the unfiltered, server-ordered (by Score) single-source results from the last search - Results is re-derived from this every time SortMode or MinimumCoverage changes, rather than the search re-running.</summary>
    private List<SearchResultRowViewModel> _allResults = [];

    public static IReadOnlyList<ResultSortMode> SortModeOptions { get; } = Enum.GetValues<ResultSortMode>();

    /// <summary>#25 exit criterion 2: sorting by coverage instead of score, without re-running the search.</summary>
    [ObservableProperty]
    private ResultSortMode _sortMode = ResultSortMode.Score;

    partial void OnSortModeChanged(ResultSortMode value) => ApplyResultSortAndFilter();

    /// <summary>
    /// #25 exit criterion 2: hides results below this fraction of the query covered. 0 shows
    /// everything (the default - filtering is opt-in, matching how scoping already works in this
    /// app). Not the same axis as Score's own MinimumScore threshold: a result can score well by
    /// being cheap-per-phone while covering very little of the query.
    /// </summary>
    [ObservableProperty]
    private double _minimumCoverage;

    partial void OnMinimumCoverageChanged(double value) => ApplyResultSortAndFilter();

    /// <summary>Milestone 15 (#15): the result the shared Inspector panel currently shows, bound to the results ListBox's SelectedItem.</summary>
    [ObservableProperty]
    private SearchResultRowViewModel? _selectedResult;

    public ObservableCollection<SearchHistoryEntry> RecentSearches { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync() => await RunSearchAsync(scopeOverride: null);

    /// <summary>
    /// Addendum §25: when a scoped search finds nothing, offer the full corpus rather than leaving
    /// the user to guess whether "no matches" means the query is wrong or the scope excluded the
    /// answer. Always widens to AllIndexedMedia, never a scope-restoring rerun.
    /// </summary>
    [RelayCommand]
    private async Task SearchFullCorpusAsync() => await RunSearchAsync(scopeOverride: new SearchScope.AllIndexedMedia());

    /// <summary>
    /// Milestone 13: reproduces the scope the entry was actually run with (SearchHistoryEntry.
    /// ToSearchScope), not whatever the library panel's checkboxes currently show - "History
    /// entries round-trip their scope" per #13's exit criteria.
    /// </summary>
    [RelayCommand]
    private async Task RerunSearchAsync(SearchHistoryEntry entry)
    {
        // GetRecentAsync already excludes template-driven entries (#21 - they have no text query
        // to restore), but defend anyway rather than assign a null into this non-nullable property.
        QueryText = entry.QueryText ?? "";
        IsCompositeMode = entry.IsComposite;
        await RunSearchAsync(entry.ToSearchScope());
    }

    private async Task RunSearchAsync(SearchScope? scopeOverride)
    {
        IsBusy = true;
        StatusMessage = "Searching...";
        CanWidenToFullCorpus = false;

        try
        {
            // Milestone 7: goes through the same query-representation cache the search services
            // use, so this call and the one inside SearchAsync/SearchCompositeAsync below (same
            // query text/language, one request apart) don't phonemize the query twice.
            var phonemized = await queryCache.GetOrAddAsync(
                QueryText, Language, ct => phonemizer.PhonemizeAsync(QueryText, Language, ct));
            QueryIpa = phonemized.Ipa;

            var (scope, description, idsForHistory) = scopeOverride is null
                ? await ResolveLiveScopeAsync()
                : await DescribeExplicitScopeAsync(scopeOverride);
            ScopeSummary = description;

            var resultCount = IsCompositeMode
                ? await SearchCompositeAsync(scope)
                : await SearchSingleSourceAsync(scope);

            // Only offer widening when scoping is the reason there's nothing to show - a search
            // that already covers the whole corpus has nowhere wider to go.
            CanWidenToFullCorpus = resultCount == 0 && scope is SearchScope.SelectedMedia;

            await searchHistoryService.RecordAsync(
                QueryText, Language, IsCompositeMode, description, resultCount, idsForHistory);
            await LoadRecentSearchesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The live scope from the library panel's checkboxes right now. Selecting everything reports
    /// as AllIndexedMedia (not a same-length SelectedMedia list) so history and reruns resolve
    /// against whatever is indexed *at rerun time* - matching the pre-#13 "All indexed media"
    /// behaviour by default, and not silently freezing a scope the user never meant to narrow.
    /// </summary>
    private async Task<(SearchScope Scope, string Description, IReadOnlyCollection<Guid>? IdsForHistory)> ResolveLiveScopeAsync()
    {
        var (selectedIds, total) = await libraryService.GetSelectionSummaryAsync();

        if (selectedIds.Count == total)
        {
            return (new SearchScope.AllIndexedMedia(), "All indexed media", null);
        }

        // Milestone 17 (#20): a catalog applied to the scope reads as "Catalog: name (n sources)"
        // rather than the generic count - see LibraryService.ActiveCatalogLabel.
        var description = libraryService.ActiveCatalogLabel is { } catalogLabel
            ? $"Catalog: {catalogLabel} ({selectedIds.Count} source(s))"
            : $"{selectedIds.Count} of {total} source(s)";

        return (new SearchScope.SelectedMedia(selectedIds), description, selectedIds);
    }

    private async Task<(SearchScope Scope, string Description, IReadOnlyCollection<Guid>? IdsForHistory)> DescribeExplicitScopeAsync(SearchScope scope)
    {
        if (scope is SearchScope.SelectedMedia selected)
        {
            var (_, total) = await libraryService.GetSelectionSummaryAsync();
            return (scope, $"{selected.MediaIds.Count} of {total} source(s)", selected.MediaIds);
        }

        return (scope, "All indexed media", null);
    }

    /// <summary>Refreshes the scope indicator without running a search - called when this tab is first shown, so it isn't blank until the user searches.</summary>
    [RelayCommand]
    public async Task RefreshScopeSummaryAsync()
    {
        var (_, description, _) = await ResolveLiveScopeAsync();
        ScopeSummary = description;
    }

    [RelayCommand]
    public async Task LoadRecentSearchesAsync()
    {
        var recent = await searchHistoryService.GetRecentAsync();

        RecentSearches.Clear();
        foreach (var entry in recent)
        {
            RecentSearches.Add(entry);
        }
    }

    private async Task<int> SearchSingleSourceAsync(SearchScope scope)
    {
        CompositeResults.Clear();

        var results = await searchService.SearchAsync(QueryText, Language, scope);
        var mediaPaths = await libraryService.GetPathsAsync(results.Select(r => r.MediaId));

        SelectedResult = null;
        _allResults = results
            .Select(result => new SearchResultRowViewModel(result, playerLauncher, clipboard, clipExtractor, filePicker)
            {
                MediaPath = mediaPaths.GetValueOrDefault(result.MediaId),
            })
            .ToList();
        ApplyResultSortAndFilter();

        StatusMessage = _allResults.Count > 0
            ? $"{_allResults.Count} result(s)."
            : "No matches found.";

        return _allResults.Count;
    }

    /// <summary>
    /// #25: re-derives the displayed Results from _allResults per SortMode/MinimumCoverage, without
    /// re-running the search - Score order is exactly what the server already returned (re-sorting
    /// by the same key the server used would just be redundant work), Coverage order is computed
    /// client-side since the server has no notion of it to sort by.
    /// </summary>
    private void ApplyResultSortAndFilter()
    {
        var previousSelection = SelectedResult;
        Results.Clear();
        foreach (var row in ResultSortFilter.Apply(_allResults, SortMode, MinimumCoverage))
        {
            Results.Add(row);
        }

        SelectedResult = previousSelection is not null && Results.Contains(previousSelection) ? previousSelection : null;
    }

    private async Task<int> SearchCompositeAsync(SearchScope scope)
    {
        SelectedResult = null;
        Results.Clear();
        _allResults = [];

        var results = await compositeSearchService.SearchAsync(QueryText, Language, scope);
        var allMediaIds = results.SelectMany(r => r.Components.Select(c => c.MediaId)).Distinct().ToList();
        var mediaTitles = await libraryService.GetTitlesAsync(allMediaIds);
        var mediaPaths = await libraryService.GetPathsAsync(allMediaIds);

        CompositeResults.Clear();
        foreach (var result in results)
        {
            CompositeResults.Add(new CompositeSearchResultRowViewModel(result, mediaTitles, mediaPaths, clipExtractor, filePicker));
        }

        StatusMessage = CompositeResults.Count > 0
            ? $"{CompositeResults.Count} composite result(s)."
            : "No composite matches found.";

        return CompositeResults.Count;
    }

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(QueryText);
}
